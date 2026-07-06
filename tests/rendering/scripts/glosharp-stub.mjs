#!/usr/bin/env node
// Fake `glosharp` executable that serves committed fixtures instead of
// running Roslyn. The EC plugin (via @glosharp/core) spawns this with
// `process --stdin`, writes the code block to stdin, and expects
// GloSharpResult JSON on stdout. We match the incoming source against the
// fixtures' `source` field (modulo trailing whitespace) so gallery builds
// are fixture-only and never require the .NET SDK.
import { readdirSync, readFileSync } from 'node:fs'
import { join, resolve, dirname } from 'node:path'
import { fileURLToPath } from 'node:url'

const FIXTURES_DIR = resolve(dirname(fileURLToPath(import.meta.url)), '../fixtures')

let input = ''
process.stdin.setEncoding('utf-8')
for await (const chunk of process.stdin) input += chunk

const wanted = input.replace(/\s+$/, '')

for (const file of readdirSync(FIXTURES_DIR).filter(f => f.endsWith('.json')).sort()) {
  const fixture = JSON.parse(readFileSync(join(FIXTURES_DIR, file), 'utf-8'))
  if (fixture.source.replace(/\s+$/, '') === wanted) {
    process.stdout.write(JSON.stringify(fixture.result))
    process.exit(0)
  }
}

console.error('glosharp-stub: no fixture matches the given source; run fixtures:update if you added a sample')
process.exit(1)
