import { createHash } from 'node:crypto'
import { stat } from 'node:fs/promises'
import path from 'node:path'
import { checkCliVersion } from './compat.js'
import { GloSharpCliError } from './errors.js'
import { resolveExecutable, type ResolvedExecutable } from './executable.js'
import { globalLimiter, Limiter } from './limiter.js'
import { defaultWorkers, FALLBACK, getPool, type ServeCall, type ServeRequestOptions } from './pool.js'
import { runCli } from './spawn.js'
import type { GloSharpOptions, GloSharpProcessOptions, GloSharpRenderOptions, GloSharpResult } from './types.js'

const DEFAULT_TIMEOUT_MS = 180_000
const DEFAULT_CACHE_SIZE = 1000

/** The CLI options a call can set; also the `glosharp serve` request option names. */
interface CliOptions {
  framework?: string
  project?: string
  region?: string
  noRestore?: boolean
  cacheDir?: string
  config?: string
  complog?: string
  complogProject?: string
  theme?: string
  standalone?: boolean
  noStyles?: boolean
}

const RENDER_ONLY = new Set(['theme', 'standalone', 'noStyles'])

const CLI_FLAGS: Record<keyof CliOptions, string> = {
  framework: '--framework',
  project: '--project',
  region: '--region',
  noRestore: '--no-restore',
  cacheDir: '--cache-dir',
  config: '--config',
  complog: '--complog',
  complogProject: '--complog-project',
  theme: '--theme',
  standalone: '--standalone',
  noStyles: '--no-styles',
}

export interface GloSharpInstance {
  /** Compile a snippet (or file) and return the extracted type information. */
  process(options: GloSharpProcessOptions): Promise<GloSharpResult>
  /** Render a snippet (or file) to a self-contained HTML fragment with the CLI's renderer. */
  render(options: GloSharpRenderOptions): Promise<string>
  /** Drop every cached result. */
  clearCache(): void
}

export function createGloSharp(options: GloSharpOptions = {}): GloSharpInstance {
  const cache = new LruCache<Promise<GloSharpResult>>(options.cacheSize ?? DEFAULT_CACHE_SIZE)
  const htmlCache = new LruCache<Promise<string>>(options.cacheSize ?? DEFAULT_CACHE_SIZE)
  const instanceLimiter = options.concurrency !== undefined ? new Limiter(options.concurrency) : undefined

  /**
   * The CLI options for a call, per-call values over instance defaults, in
   * command-line order. Shared by the one-shot command line and `serve`
   * requests so both paths see exactly the same settings.
   */
  function cliOptions(opts: GloSharpRenderOptions): CliOptions {
    return {
      framework: opts.framework ?? options.framework,
      project: opts.project,
      // Region extraction is text-based, so it works with --stdin as well as files.
      region: opts.region,
      noRestore: opts.noRestore,
      cacheDir: opts.cacheDir ?? options.cacheDir,
      config: opts.configFile ?? options.configFile,
      complog: opts.complog ?? options.complog,
      complogProject: opts.complogProject ?? options.complogProject,
      theme: opts.theme,
      standalone: opts.standalone,
      noStyles: opts.noStyles,
    }
  }

  function buildArgs(command: 'process' | 'render', opts: GloSharpRenderOptions): string[] {
    const args: string[] = [command, opts.file ? opts.file : '--stdin']
    for (const [name, value] of Object.entries(cliOptions(opts))) {
      if (!value) continue
      if (command !== 'render' && RENDER_ONLY.has(name)) continue
      const flag = CLI_FLAGS[name as keyof CliOptions]
      if (value === true) args.push(flag)
      else args.push(flag, value)
    }
    return args
  }

  /** The same settings as a `glosharp serve` request. */
  function serveCall(command: 'process' | 'render', args: string[], opts: GloSharpRenderOptions, exe: ResolvedExecutable): ServeCall {
    const serveOptions: ServeRequestOptions = {}
    if (opts.file) serveOptions.file = opts.file
    for (const [name, value] of Object.entries(cliOptions(opts))) {
      if (!value) continue
      if (command !== 'render' && RENDER_ONLY.has(name)) continue
      serveOptions[name] = value
    }
    // Relative paths and config discovery follow this process's working directory,
    // as they do for a CLI spawned per snippet.
    serveOptions.cwd = globalThis.process.cwd()
    return {
      code: opts.file ? undefined : (opts.code ?? ''),
      options: serveOptions,
      timeoutMs: timeoutFor(opts),
      signal: opts.signal,
      snippet: opts.code ?? opts.file,
      command: exe.command,
      args: [...exe.prefix, ...args],
    }
  }

  function timeoutFor(opts: GloSharpProcessOptions): number {
    if (opts.timeoutMs !== undefined) return opts.timeoutMs
    if (options.timeoutMs !== undefined) return options.timeoutMs
    const fromEnv = Number(globalThis.process.env.GLOSHARP_TIMEOUT_MS)
    return Number.isFinite(fromEnv) && fromEnv >= 0 ? fromEnv : DEFAULT_TIMEOUT_MS
  }

  /**
   * Run a command on a `glosharp serve` worker when the CLI supports it (the
   * parsed result), else as a CLI process of its own (its stdout).
   */
  async function run(
    command: 'process' | 'render',
    args: string[],
    opts: GloSharpRenderOptions,
  ): Promise<{ served: unknown } | { stdout: string }> {
    const exe = await resolveExecutable(options.executable)
    void checkCliVersion(exe) // once per executable, in the background; warns on a mismatch
    const workers = options.workers ?? defaultWorkers()
    const task = () =>
      globalLimiter.run(async () => {
        if (workers > 0) {
          const served = await getPool(exe, workers).request(command, serveCall(command, args, opts, exe))
          if (served !== FALLBACK) return { served }
        }
        const { stdout } = await runCli(exe.command, [...exe.prefix, ...args], {
          stdin: opts.file ? undefined : (opts.code ?? ''),
          timeoutMs: timeoutFor(opts),
          signal: opts.signal,
          snippet: opts.code ?? opts.file,
        })
        return { stdout }
      })
    return instanceLimiter ? instanceLimiter.run(task) : task()
  }

  /**
   * Cache key covering everything that can change the output: the code (or the
   * file's identity, size and mtime), every CLI argument, and the working
   * directory the CLI discovers `glosharp.config.json` from. `undefined` when
   * the input can't be fingerprinted (missing file) — the CLI reports that.
   */
  async function cacheKey(args: string[], opts: GloSharpProcessOptions): Promise<string | undefined> {
    const hash = createHash('sha256')
    hash.update(JSON.stringify({ args, cwd: globalThis.process.cwd() }))
    if (opts.file) {
      const absolute = path.resolve(opts.file)
      try {
        const info = await stat(absolute)
        hash.update(`\u0000file:${absolute}:${info.size}:${info.mtimeMs}`)
      } catch {
        return undefined
      }
    } else {
      hash.update('\u0000code:').update(opts.code ?? '', 'utf8')
    }
    return hash.digest('hex')
  }

  async function cached<T>(
    store: LruCache<Promise<T>>,
    key: string | undefined,
    produce: () => Promise<T>,
  ): Promise<T> {
    if (key === undefined) return produce()
    const hit = store.get(key)
    if (hit) return hit
    // Store the promise, not the value, so concurrent identical requests share
    // one CLI run. Failures are evicted so a retry runs again.
    const pending = produce()
    store.set(key, pending)
    pending.catch(() => store.delete(key))
    return pending
  }

  async function processSnippet(opts: GloSharpProcessOptions): Promise<GloSharpResult> {
    const args = buildArgs('process', opts)
    return cached(cache, await cacheKey(args, opts), async () => {
      const output = await run('process', args, opts)
      if ('stdout' in output) return normalizeResult(parseResult(output.stdout, opts))
      if (!output.served || typeof output.served !== 'object' || Array.isArray(output.served)) {
        throw invalidServeOutput(output.served, opts)
      }
      return normalizeResult(output.served as GloSharpResult)
    })
  }

  async function render(opts: GloSharpRenderOptions): Promise<string> {
    const args = buildArgs('render', opts)
    return cached(htmlCache, await cacheKey(args, opts), async () => {
      const output = await run('render', args, opts)
      if ('stdout' in output) return output.stdout
      if (typeof output.served !== 'string') throw invalidServeOutput(output.served, opts)
      return output.served
    })
  }

  return {
    process: processSnippet,
    render,
    clearCache() {
      cache.clear()
      htmlCache.clear()
    },
  }
}

function parseResult(stdout: string, opts: GloSharpProcessOptions): GloSharpResult {
  try {
    return JSON.parse(stdout) as GloSharpResult
  } catch (error) {
    const excerpt = stdout.length > 500 ? `${stdout.slice(0, 500)}… (${stdout.length} bytes)` : stdout
    throw new GloSharpCliError(`glosharp produced invalid JSON:\n${excerpt}`, {
      kind: 'invalid-output',
      snippet: opts.code ?? opts.file,
      cause: error,
    })
  }
}

function invalidServeOutput(value: unknown, opts: GloSharpProcessOptions): GloSharpCliError {
  const text = JSON.stringify(value) ?? String(value)
  const excerpt = text.length > 500 ? `${text.slice(0, 500)}… (${text.length} bytes)` : text
  return new GloSharpCliError(`glosharp serve returned an unexpected result:\n${excerpt}`, {
    kind: 'invalid-output',
    snippet: opts.code ?? opts.file,
  })
}

/** Fill in fields older CLIs omit, so consumers can rely on them. */
function normalizeResult(result: GloSharpResult): GloSharpResult {
  result.hovers ??= []
  result.errors ??= []
  result.hiddenErrors ??= []
  result.completions ??= []
  result.highlights ??= []
  result.tags ??= []
  result.hidden ??= []
  if (result.meta) result.meta.warnings ??= []
  return result
}

/** A Map with a size bound that evicts the least recently used entry. */
class LruCache<V> {
  readonly #max: number
  readonly #map = new Map<string, V>()

  constructor(max: number) {
    this.#max = Math.max(1, max)
  }

  get(key: string): V | undefined {
    const value = this.#map.get(key)
    if (value !== undefined) {
      this.#map.delete(key)
      this.#map.set(key, value)
    }
    return value
  }

  set(key: string, value: V): void {
    this.#map.delete(key)
    this.#map.set(key, value)
    while (this.#map.size > this.#max) {
      this.#map.delete(this.#map.keys().next().value!)
    }
  }

  delete(key: string): void {
    this.#map.delete(key)
  }

  clear(): void {
    this.#map.clear()
  }
}
