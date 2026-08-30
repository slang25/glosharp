import { createHash } from 'node:crypto'
import { existsSync } from 'node:fs'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { createGloSharp } from '@glosharp/core'
import { buildArtifacts, collectSnippets, type BuildResult } from './artifacts.js'
import { AUTO_THEME } from './config.js'
import { startDevServer } from './dev-server.js'
import { DEFAULT_FENCE } from './fence.js'
import { DEFAULT_THEMES } from './frame.js'
import { collectMarkdownFiles } from './paths.js'

const USAGE = `glosharp-gitbook — publish pre-rendered Glo# snippets for the GitBook integration

Usage:
  glosharp-gitbook build <paths...> --out <dir> [options]
  glosharp-gitbook dev   <paths...> [--port <n>] [options]
  glosharp-gitbook scan  <paths...> [--fence <lang>] [--json]

<paths...> are Markdown files or directories to scan (directories are walked
recursively for .md/.mdx).

Build options:
  --out <dir>          Directory to publish artifacts into (required)
  --fence <lang>       Fence language to claim (default: ${DEFAULT_FENCE})
  --theme <name>       Theme to render; repeatable (default: ${DEFAULT_THEMES.join(', ')})
  --concurrency <n>    Concurrent renders (default: 4)
  --check              Report drift without writing; exit 1 if anything changed
  --skip-existing      Reuse artifacts already on disk instead of re-rendering
  --prune              Delete artifacts no snippet claims

Dev options (preview your snippets the way the GitBook block shows them,
without a GitBook account — edit Markdown, reload the page):
  --port <n>           Port to listen on (default: 4180)
  --frame-theme <t>    Theme the preview requests: auto, github-dark,
                       github-light (default: auto, follows your browser)
  --out <dir>          Artifacts directory (default: a stable temp directory)
  --fresh              Re-render every snippet on reload, not just new ones
  --no-build           Serve existing artifacts without rendering

Compilation options (forwarded to the glosharp CLI):
  --framework <tfm>    Target framework, e.g. net10.0
  --project <path>     .csproj providing NuGet context
  --complog <path>     .complog / .glocontext providing compilation context
  --config <path>      glosharp.config.json to load
  --cache-dir <path>   Result cache directory
  --executable <path>  Path to the glosharp executable
`

interface ParsedArgs {
  command?: string
  paths: string[]
  flags: Map<string, string[]>
  bools: Set<string>
}

const VALUE_FLAGS = new Set([
  'out',
  'fence',
  'theme',
  'concurrency',
  'framework',
  'project',
  'complog',
  'config',
  'cache-dir',
  'executable',
  'port',
  'frame-theme',
])
const BOOL_FLAGS = new Set([
  'check',
  'skip-existing',
  'prune',
  'fresh',
  'no-build',
  'json',
  'help',
])

export function parseArgs(argv: string[]): ParsedArgs {
  const parsed: ParsedArgs = { paths: [], flags: new Map(), bools: new Set() }

  for (let i = 0; i < argv.length; i++) {
    const arg = argv[i]
    if (arg === '--help' || arg === '-h') {
      parsed.bools.add('help')
      continue
    }
    if (arg.startsWith('--')) {
      const name = arg.slice(2)
      if (BOOL_FLAGS.has(name)) {
        parsed.bools.add(name)
        continue
      }
      if (!VALUE_FLAGS.has(name)) throw new Error(`Unknown option: ${arg}`)
      const value = argv[++i]
      if (value === undefined) throw new Error(`Option ${arg} requires a value`)
      parsed.flags.set(name, [...(parsed.flags.get(name) ?? []), value])
      continue
    }
    if (parsed.command === undefined) parsed.command = arg
    else parsed.paths.push(arg)
  }

  return parsed
}

export async function run(argv: string[]): Promise<number> {
  let args: ParsedArgs
  try {
    args = parseArgs(argv)
  } catch (error) {
    process.stderr.write(`${error instanceof Error ? error.message : String(error)}\n\n${USAGE}`)
    return 1
  }

  if (args.bools.has('help')) {
    process.stdout.write(USAGE)
    return 0
  }

  if (args.command === undefined) {
    process.stderr.write(USAGE)
    return 1
  }

  const first = (name: string): string | undefined => args.flags.get(name)?.[0]
  const fence = first('fence') ?? DEFAULT_FENCE

  // A bare command name is resolved on PATH, but an explicit path that does not
  // exist is a typo or an unbuilt CLI — say so now rather than at spawn time.
  const executable = first('executable')
  if (executable?.includes(path.sep) && !existsSync(executable)) {
    process.stderr.write(
      `--executable ${executable} does not exist.\n` +
        `Install the CLI (dotnet tool install -g GloSharp.Cli), or point at a build ` +
        `of it (dotnet build src/GloSharp.Cli -c Release).\n`,
    )
    return 1
  }

  if (args.paths.length === 0) {
    process.stderr.write(`No paths given.\n\n${USAGE}`)
    return 1
  }

  const root = process.cwd()
  const files = await collectMarkdownFiles(args.paths)

  if (args.command === 'scan') {
    const snippets = await collectSnippets({ files, root, fence })
    if (args.bools.has('json')) {
      process.stdout.write(`${JSON.stringify(snippets, null, 2)}\n`)
    } else {
      for (const snippet of snippets) {
        const where = snippet.occurrences.map((o) => `${o.file}:${o.line}`).join(', ')
        process.stdout.write(`${snippet.key}  ${where}\n`)
      }
      process.stdout.write(
        `\n${snippets.length} snippet(s) in ${files.length} file(s) (fence: ${fence})\n`,
      )
    }
    return 0
  }

  if (args.command !== 'build' && args.command !== 'dev') {
    process.stderr.write(`Unknown command: ${args.command}\n\n${USAGE}`)
    return 1
  }

  const concurrencyRaw = first('concurrency')
  const concurrency = concurrencyRaw === undefined ? 4 : Number(concurrencyRaw)
  if (!Number.isInteger(concurrency) || concurrency < 1) {
    process.stderr.write(`--concurrency must be a positive integer\n`)
    return 1
  }

  const glosharp = createGloSharp({
    executable,
    framework: first('framework'),
    cacheDir: first('cache-dir'),
    configFile: first('config'),
    complog: first('complog'),
  })
  const project = first('project')
  const render = ({ code, theme, framework }: RenderInput) =>
    glosharp.render({ code, theme, framework, project })

  if (args.command === 'dev') {
    const portRaw = first('port')
    const port = portRaw === undefined ? 4180 : Number(portRaw)
    if (!Number.isInteger(port) || port < 0 || port > 65535) {
      process.stderr.write(`--port must be a port number\n`)
      return 1
    }

    const previewOut = first('out') ?? defaultPreviewDir(root)
    let server: Awaited<ReturnType<typeof startDevServer>>
    try {
      server = await startDevServer({
        paths: args.paths,
        root,
        outDir: previewOut,
        fence,
        theme: first('frame-theme') ?? AUTO_THEME,
        themes: args.flags.get('theme'),
        concurrency,
        fresh: args.bools.has('fresh'),
        noBuild: args.bools.has('no-build'),
        port,
        log: (message) => process.stderr.write(`${message}\n`),
        render,
      })
    } catch (error) {
      process.stderr.write(`Error: ${error instanceof Error ? error.message : String(error)}\n`)
      return 1
    }

    process.stderr.write(
      `\nGlo# preview: ${server.url}\n` +
        `Artifacts:    ${previewOut}\n` +
        `Snippets render on first load. Edit your Markdown and reload. Ctrl-C to stop.\n\n`,
    )

    await new Promise<void>((resolve) => {
      process.once('SIGINT', resolve)
      process.once('SIGTERM', resolve)
    })
    await server.close()
    process.stderr.write('Stopped.\n')
    return 0
  }

  const outDir = first('out')
  if (!outDir) {
    process.stderr.write(`build requires --out <dir>\n\n${USAGE}`)
    return 1
  }

  let result: BuildResult
  try {
    result = await buildArtifacts({
      files,
      root,
      outDir,
      fence,
      themes: args.flags.get('theme'),
      concurrency,
      check: args.bools.has('check'),
      skipExisting: args.bools.has('skip-existing'),
      prune: args.bools.has('prune'),
      log: (message) => process.stderr.write(`${message}\n`),
      render,
    })
  } catch (error) {
    process.stderr.write(`Error: ${error instanceof Error ? error.message : String(error)}\n`)
    return 1
  }

  const verb = args.bools.has('check') ? 'would change' : 'wrote'
  process.stderr.write(
    `${result.snippets.length} snippet(s) in ${files.length} file(s); ` +
      `${verb} ${result.changed.length}, reused ${result.unchanged.length}\n`,
  )

  // Pruning is reported by the builder as it happens; only the "you have
  // leftovers" hint belongs here.
  if (!args.bools.has('prune')) {
    for (const orphan of result.orphaned) {
      process.stderr.write(`orphaned (use --prune): ${orphan}\n`)
    }
  }

  for (const failure of result.failures) {
    process.stderr.write(`failed ${failure.theme}/${failure.key}: ${failure.message}\n`)
  }
  if (result.failures.length > 0) return 1

  if (args.bools.has('check') && result.changed.length > 0) {
    for (const change of result.changed) process.stderr.write(`drift: ${change}\n`)
    return 1
  }

  return 0
}

type RenderInput = { code: string; theme: string; framework?: string }

/**
 * Where `dev` keeps its artifacts when none is given: a per-project directory
 * under the system temp dir, so repeat previews reuse renders without leaving
 * anything in the repo.
 */
function defaultPreviewDir(root: string): string {
  const id = createHash('sha256').update(root).digest('hex').slice(0, 12)
  return path.join(tmpdir(), `glosharp-gitbook-preview-${id}`)
}
