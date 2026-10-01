import { createHash } from 'node:crypto'
import { stat } from 'node:fs/promises'
import path from 'node:path'
import { GloSharpCliError } from './errors.js'
import { resolveExecutable } from './executable.js'
import { globalLimiter, Limiter } from './limiter.js'
import { runCli } from './spawn.js'
import type { GloSharpOptions, GloSharpProcessOptions, GloSharpRenderOptions, GloSharpResult } from './types.js'

const DEFAULT_TIMEOUT_MS = 180_000
const DEFAULT_CACHE_SIZE = 1000

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

  function buildArgs(command: string, opts: GloSharpProcessOptions): string[] {
    const args = [command]

    if (opts.file) {
      args.push(opts.file)
    } else {
      args.push('--stdin')
    }

    const framework = opts.framework ?? options.framework
    if (framework) {
      args.push('--framework', framework)
    }

    if (opts.project) {
      args.push('--project', opts.project)
    }

    // Region extraction is text-based, so it works with --stdin as well as files.
    if (opts.region) {
      args.push('--region', opts.region)
    }

    if (opts.noRestore) {
      args.push('--no-restore')
    }

    const cacheDir = opts.cacheDir ?? options.cacheDir
    if (cacheDir) {
      args.push('--cache-dir', cacheDir)
    }

    const configFile = opts.configFile ?? options.configFile
    if (configFile) {
      args.push('--config', configFile)
    }

    const complog = opts.complog ?? options.complog
    if (complog) {
      args.push('--complog', complog)
    }

    const complogProject = opts.complogProject ?? options.complogProject
    if (complogProject) {
      args.push('--complog-project', complogProject)
    }

    return args
  }

  function timeoutFor(opts: GloSharpProcessOptions): number {
    if (opts.timeoutMs !== undefined) return opts.timeoutMs
    if (options.timeoutMs !== undefined) return options.timeoutMs
    const fromEnv = Number(globalThis.process.env.GLOSHARP_TIMEOUT_MS)
    return Number.isFinite(fromEnv) && fromEnv >= 0 ? fromEnv : DEFAULT_TIMEOUT_MS
  }

  async function run(args: string[], opts: GloSharpProcessOptions): Promise<string> {
    const { command, prefix } = await resolveExecutable(options.executable)
    const task = () =>
      globalLimiter.run(() =>
        runCli(command, [...prefix, ...args], {
          stdin: opts.file ? undefined : (opts.code ?? ''),
          timeoutMs: timeoutFor(opts),
          signal: opts.signal,
          snippet: opts.code ?? opts.file,
        }),
      )
    const { stdout } = await (instanceLimiter ? instanceLimiter.run(task) : task())
    return stdout
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
      const stdout = await run(args, opts)
      return normalizeResult(parseResult(stdout, opts))
    })
  }

  async function render(opts: GloSharpRenderOptions): Promise<string> {
    const args = buildArgs('render', opts)
    if (opts.theme) args.push('--theme', opts.theme)
    if (opts.standalone) args.push('--standalone')
    return cached(htmlCache, await cacheKey(args, opts), () => run(args, opts))
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
