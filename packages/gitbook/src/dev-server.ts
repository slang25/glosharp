import { createServer, type IncomingMessage, type Server, type ServerResponse } from 'node:http'
import { readFile } from 'node:fs/promises'
import path from 'node:path'
import { buildArtifacts, collectSnippets, type RenderSnippet, type Snippet } from './artifacts.js'
import { AUTO_THEME } from './config.js'
import { DEFAULT_FENCE } from './fence.js'
import { renderFrameShell } from './frame.js'
import { renderDevHost, renderDevHostError, type DevHostCase } from './dev-host.js'
import { collectMarkdownFiles } from './paths.js'

export interface DevServerOptions {
  /** Markdown files or directories to preview. */
  paths: string[]
  /** Root that occurrence paths are shown relative to. Defaults to `process.cwd()`. */
  root?: string
  /** Where artifacts are built to and served from. */
  outDir: string
  fence?: string
  /** Theme handed to the frame; `auto` follows the browser's colour scheme. */
  theme?: string
  /** Themes to render. Defaults to both built-in themes. */
  themes?: string[]
  concurrency?: number
  /** Re-render everything on each reload instead of only new snippets. */
  fresh?: boolean
  /** Skip building entirely and serve whatever is already in `outDir`. */
  noBuild?: boolean
  port?: number
  host?: string
  render: RenderSnippet
  log?: (message: string) => void
}

export interface DevServer {
  url: string
  port: number
  close(): Promise<void>
}

const FRAME_PATH = '/frame'
const ARTIFACTS_PREFIX = '/artifacts/'

/**
 * Serve the GitBook block's runtime locally: the real webframe shell, the real
 * artifacts, and a host page implementing GitBook's side of the contract.
 *
 * Every request for `/` re-scans and rebuilds, so the loop is "edit Markdown,
 * reload the page". Only snippets whose text changed are re-rendered, because a
 * changed snippet is a new artifact key.
 */
export async function startDevServer(options: DevServerOptions): Promise<DevServer> {
  const root = options.root ?? process.cwd()
  const fence = options.fence ?? DEFAULT_FENCE
  const theme = options.theme ?? AUTO_THEME
  const outDir = path.resolve(options.outDir)
  const log = options.log ?? (() => {})
  const shell = renderFrameShell()

  async function preview(): Promise<string> {
    const files = await collectMarkdownFiles(options.paths)
    let snippets: Snippet[]

    try {
      snippets = await collectSnippets({ files, root, fence })
      if (!options.noBuild) {
        const result = await buildArtifacts({
          files,
          root,
          outDir,
          fence,
          themes: options.themes,
          concurrency: options.concurrency,
          skipExisting: !options.fresh,
          render: options.render,
          log,
        })
        if (result.failures.length > 0) {
          return renderDevHostError(
            result.failures.map((f) => `${f.theme}/${f.key}: ${f.message}`).join('\n\n'),
            'One or more snippets failed to render.',
          )
        }
      }
    } catch (error) {
      return renderDevHostError(
        error instanceof Error ? error.message : String(error),
        'The scan or the build failed before anything could be previewed.',
      )
    }

    const cases: DevHostCase[] = snippets.map((snippet) => ({
      id: snippet.key,
      title: snippet.occurrences.map((o) => `${o.file}:${o.line}`).join(', '),
      detail: `${snippet.key.slice(0, 16)}…`,
      state: { content: snippet.code, artifacts: ARTIFACTS_PREFIX.replace(/\/$/, ''), theme },
    }))

    return renderDevHost({
      cases,
      frameUrl: FRAME_PATH,
      summary:
        `${snippets.length} snippet(s) in ${files.length} file(s) · fence \`${fence}\` · ` +
        `theme ${theme} · artifacts ${outDir}` +
        (options.noBuild ? ' · serving existing artifacts (--no-build)' : ''),
    })
  }

  const server = createServer((request, response) => {
    handle(request, response).catch((error: unknown) => {
      send(response, 500, 'text/plain; charset=utf-8', String(error))
    })
  })

  async function handle(request: IncomingMessage, response: ServerResponse): Promise<void> {
    const url = new URL(request.url ?? '/', 'http://localhost')

    if (url.pathname === '/') {
      send(response, 200, 'text/html; charset=utf-8', await preview())
      return
    }

    if (url.pathname === FRAME_PATH) {
      send(response, 200, 'text/html; charset=utf-8', shell)
      return
    }

    if (url.pathname.startsWith(ARTIFACTS_PREFIX)) {
      const relative = decodeURIComponent(url.pathname.slice(ARTIFACTS_PREFIX.length))
      const file = path.resolve(outDir, relative)
      // Serve only from inside the artifacts directory.
      if (file !== outDir && !file.startsWith(outDir + path.sep)) {
        send(response, 403, 'text/plain; charset=utf-8', 'forbidden')
        return
      }
      try {
        const body = await readFile(file, 'utf8')
        const type = file.endsWith('.json') ? 'application/json' : 'text/html; charset=utf-8'
        send(response, 200, type, body)
      } catch {
        send(response, 404, 'text/plain; charset=utf-8', 'not found')
      }
      return
    }

    send(response, 404, 'text/plain; charset=utf-8', 'not found')
  }

  const port = await listen(server, options.port ?? 4180, options.host ?? '127.0.0.1')

  return {
    port,
    url: `http://${options.host ?? 'localhost'}:${port}/`,
    close: () =>
      new Promise<void>((resolve, reject) =>
        server.close((error) => (error ? reject(error) : resolve())),
      ),
  }
}

function send(response: ServerResponse, status: number, type: string, body: string): void {
  response.writeHead(status, { 'content-type': type, 'cache-control': 'no-store' })
  response.end(body)
}

function listen(server: Server, port: number, host: string): Promise<number> {
  return new Promise((resolve, reject) => {
    server.once('error', (error: NodeJS.ErrnoException) => {
      reject(
        error.code === 'EADDRINUSE'
          ? new Error(`Port ${port} is already in use. Pass --port <n> to pick another.`)
          : error,
      )
    })
    server.listen(port, host, () => {
      const address = server.address()
      resolve(typeof address === 'object' && address ? address.port : port)
    })
  })
}
