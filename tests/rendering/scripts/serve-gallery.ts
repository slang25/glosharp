// Minimal static file server for the built gallery (no dependencies).
import { createServer } from 'node:http'
import { readFile } from 'node:fs/promises'
import { join, resolve, extname, normalize } from 'node:path'

const DIST = resolve(import.meta.dirname!, '../gallery-dist')
const PORT = Number(process.env.GALLERY_PORT ?? 4173)

const MIME: Record<string, string> = {
  '.html': 'text/html; charset=utf-8',
  '.css': 'text/css; charset=utf-8',
  '.js': 'text/javascript; charset=utf-8',
  '.mjs': 'text/javascript; charset=utf-8',
  '.json': 'application/json',
  '.woff2': 'font/woff2',
  '.svg': 'image/svg+xml',
}

createServer(async (req, res) => {
  try {
    const url = new URL(req.url ?? '/', 'http://localhost')
    let path = normalize(decodeURIComponent(url.pathname)).replace(/^(\.\.[/\\])+/, '')
    if (path === '/' || path === '\\') path = '/index.html'
    const file = join(DIST, path)
    if (!file.startsWith(DIST)) throw new Error('forbidden')
    const body = await readFile(file)
    res.writeHead(200, { 'content-type': MIME[extname(file)] ?? 'application/octet-stream' })
    res.end(body)
  } catch {
    res.writeHead(404, { 'content-type': 'text/plain' })
    res.end('not found')
  }
}).listen(PORT, () => {
  console.log(`Gallery: http://localhost:${PORT}/`)
})
