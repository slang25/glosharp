// Asserts that a built site (or a single HTML file) actually contains Glo#
// hovers. Integrations fall back to plain highlighting when the CLI is
// missing or fails, so a site can build green with no type information at all;
// this turns that into a CI failure.
//
// Usage: node .github/scripts/assert-rendered.mjs <dir-or-file> [...]
import { readdirSync, readFileSync, statSync } from 'node:fs'
import { join } from 'node:path'

const HOVER = /class="[^"]*\bglosharp-hover\b/g

function htmlFiles(path) {
  if (statSync(path).isFile()) return [path]
  return readdirSync(path, { withFileTypes: true }).flatMap(entry => {
    const full = join(path, entry.name)
    if (entry.isDirectory()) return htmlFiles(full)
    return entry.name.endsWith('.html') ? [full] : []
  })
}

const targets = process.argv.slice(2)
if (targets.length === 0) {
  console.error('usage: assert-rendered.mjs <dir-or-file> [...]')
  process.exit(2)
}

let failed = false
for (const target of targets) {
  const files = htmlFiles(target)
  const hovers = files.reduce((n, f) => n + (readFileSync(f, 'utf-8').match(HOVER)?.length ?? 0), 0)
  if (hovers === 0) {
    failed = true
    console.error(`${target}: no glosharp hovers in ${files.length} HTML file(s) — did the CLI run?`)
  } else {
    console.log(`${target}: ${hovers} hovers in ${files.length} HTML file(s)`)
  }
}

process.exit(failed ? 1 : 0)
