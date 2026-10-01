// End-to-end runs of the CLI entry point against a stub `glosharp` executable.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { chmod, mkdir, mkdtemp, readFile, rm, writeFile, readdir } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { run } from '../src/cli.js'
import { snippetKey } from '../src/hash.js'
import { previewCases } from '../src/dev-server.js'

// Answers `process` with JSON (CS0029 for snippets containing "nope") and
// `render` with a fragment.
const STUB = `#!/usr/bin/env node
const command = process.argv[2]
let code = ''
process.stdin.on('data', (c) => (code += c))
process.stdin.on('end', () => {
  if (command === 'render') {
    process.stdout.write('<div class="glosharp-code">' + code + '</div>')
    return
  }
  const broken = code.includes('nope')
  process.stdout.write(JSON.stringify({
    code, original: code, lang: 'csharp', hovers: [], completions: [], highlights: [], tags: [], hidden: [],
    errors: broken ? [{ line: 0, character: 8, length: 6, sourceLine: 0, sourceCharacter: 8, code: 'CS0029',
      message: "Cannot implicitly convert type 'string' to 'int'", severity: 'error', expected: false }] : [],
    hiddenErrors: [],
    meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: !broken, warnings: [] },
  }))
})
`

let root: string
let cwd: string
let stderr: string
let executable: string

beforeEach(async () => {
  cwd = process.cwd()
  root = await mkdtemp(path.join(tmpdir(), 'glosharp-gitbook-cli-'))
  executable = path.join(root, 'bin', 'glosharp')
  await mkdir(path.dirname(executable))
  await writeFile(executable, STUB)
  await chmod(executable, 0o755)
  process.chdir(root)
  stderr = ''
  vi.spyOn(process.stderr, 'write').mockImplementation((chunk: string | Uint8Array) => {
    stderr += String(chunk)
    return true
  })
})

afterEach(async () => {
  vi.restoreAllMocks()
  process.chdir(cwd)
  await rm(root, { recursive: true, force: true })
})

async function write(relative: string, contents: string) {
  await mkdir(path.dirname(path.join(root, relative)), { recursive: true })
  await writeFile(path.join(root, relative), contents)
}

const fence = (code: string, attrs = '') => `# Doc\n\n\`\`\`glosharp${attrs ? ` ${attrs}` : ''}\n${code}\n\`\`\`\n`
const build = (...extra: string[]) =>
  run(['build', 'docs', '--out', 'out', '--theme', 'github-dark', '--executable', executable, ...extra])

describe.skipIf(process.platform === 'win32')('glosharp-gitbook build', () => {
  it('fails on unexpected compile errors, naming file:line:col', async () => {
    await write('docs/a.md', fence('var ok = 1;'))
    await write('docs/b.md', fence('int x = "nope";'))

    expect(await build()).toBe(1)
    expect(stderr).toContain('error: docs/b.md:4:9 CS0029: Cannot implicitly convert')
    expect(stderr).toContain('--allow-errors')
  })

  it('publishes them anyway with --allow-errors', async () => {
    await write('docs/b.md', fence('int x = "nope";'))

    expect(await build('--allow-errors')).toBe(0)
    expect(await readdir(path.join(root, 'out', 'github-dark'))).toEqual([`${snippetKey('int x = "nope";')}.html`])
  })

  it('--prune --check lists what would be pruned and fails, deleting nothing', async () => {
    await write('docs/a.md', fence('var ok = 1;'))
    expect(await build()).toBe(0)
    const orphan = `github-dark/${'0'.repeat(64)}.html`
    await write(`out/${orphan}`, 'old')
    stderr = ''

    expect(await build('--prune', '--check')).toBe(1)
    expect(stderr).toContain(`would prune: ${orphan}`)
    expect(await readFile(path.join(root, 'out', orphan), 'utf8')).toBe('old')
  })

  it('build without --out says so without dumping the whole usage', async () => {
    expect(await run(['build', 'docs'])).toBe(1)
    expect(stderr).toBe('build requires --out <dir>. Run glosharp-gitbook --help for usage.\n')
  })
})

describe('previewCases', () => {
  it('honours a pinned theme per fence and keeps document order', () => {
    const snippets = [
      { key: 'b'.repeat(64), code: 'b', occurrences: [{ file: 'advanced.md', line: 11 }] },
      { key: 'a'.repeat(64), code: 'a', occurrences: [{ file: 'advanced.md', line: 3, theme: 'github-light' }] },
      { key: 'c'.repeat(64), code: 'c', occurrences: [{ file: 'advanced.md', line: 19 }, { file: 'intro.md', line: 2, theme: 'github-light' }] },
    ]

    const cases = previewCases(snippets, 'auto', '/artifacts')

    expect(cases.map((c) => [c.title, c.state.theme])).toEqual([
      ['advanced.md:3', 'github-light'],
      ['advanced.md:11', 'auto'],
      ['advanced.md:19', 'auto'],
      ['intro.md:2', 'github-light'],
    ])
  })
})
