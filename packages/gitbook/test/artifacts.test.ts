import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { mkdtemp, mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { buildArtifacts, buildIndex, collectSnippets, type RenderSnippet } from '../src/artifacts.js'
import { snippetKey } from '../src/hash.js'
import { collectMarkdownFiles } from '../src/paths.js'

let root: string
let outDir: string

beforeEach(async () => {
  root = await mkdtemp(path.join(tmpdir(), 'glosharp-gitbook-'))
  outDir = path.join(root, 'out')
})

afterEach(async () => {
  await rm(root, { recursive: true, force: true })
})

async function write(relative: string, contents: string): Promise<void> {
  const target = path.join(root, relative)
  await mkdir(path.dirname(target), { recursive: true })
  await writeFile(target, contents, 'utf8')
}

function fence(code: string, attributes = ''): string {
  return `# Doc\n\n\`\`\`glosharp${attributes ? ` ${attributes}` : ''}\n${code}\n\`\`\`\n`
}

/** Stands in for `glosharp render`; records what it was asked for. */
function fakeRenderer(): RenderSnippet & { calls: { code: string; theme: string }[] } {
  const calls: { code: string; theme: string }[] = []
  const render = (async ({ code, theme }) => {
    calls.push({ code, theme })
    return `<div class="glosharp-code" data-theme="${theme}">${code}</div>`
  }) as RenderSnippet & { calls: typeof calls }
  render.calls = calls
  return render
}

async function build(options: Partial<Parameters<typeof buildArtifacts>[0]> = {}) {
  const files = await collectMarkdownFiles([root], { excludes: ['out'] })
  return buildArtifacts({
    files,
    root,
    outDir,
    themes: ['github-dark'],
    render: fakeRenderer(),
    ...options,
  })
}

describe('collectSnippets', () => {
  it('records every occurrence of a shared snippet under one key', async () => {
    await write('a.md', fence('var x = 42;'))
    await write('nested/b.md', fence('\r\nvar x = 42;\r\n'))

    const snippets = await collectSnippets({
      files: await collectMarkdownFiles([root]),
      root,
      fence: 'glosharp',
    })

    expect(snippets).toHaveLength(1)
    expect(snippets[0].key).toBe(snippetKey('var x = 42;'))
    expect(snippets[0].occurrences).toEqual([
      { file: 'a.md', line: 3 },
      { file: 'nested/b.md', line: 3 },
    ])
  })

  it('uses POSIX separators in occurrence paths', async () => {
    await write('deep/nested/c.md', fence('var x = 1;'))

    const snippets = await collectSnippets({
      files: await collectMarkdownFiles([root]),
      root,
      fence: 'glosharp',
    })

    expect(snippets[0].occurrences[0].file).toBe('deep/nested/c.md')
  })

  it('rejects identical snippets that ask for different frameworks', async () => {
    await write('a.md', fence('var x = 42;', 'framework="net9.0"'))
    await write('b.md', fence('var x = 42;', 'framework="net10.0"'))

    await expect(
      collectSnippets({ files: await collectMarkdownFiles([root]), root, fence: 'glosharp' }),
    ).rejects.toThrow(/Conflicting framework/)
  })

  it('is sorted by key so output is comparable run to run', async () => {
    await write('a.md', `${fence('var b = 2;')}${fence('var a = 1;')}`)

    const snippets = await collectSnippets({
      files: await collectMarkdownFiles([root]),
      root,
      fence: 'glosharp',
    })

    expect(snippets.map((s) => s.key)).toEqual([...snippets.map((s) => s.key)].sort())
  })
})

describe('buildArtifacts', () => {
  it('publishes one fragment per theme under <theme>/<key>.html', async () => {
    await write('a.md', fence('var x = 42;'))

    const result = await build({ themes: ['github-dark', 'github-light'] })
    const key = snippetKey('var x = 42;')

    expect(result.changed).toEqual([
      `github-dark/${key}.html`,
      `github-light/${key}.html`,
      'index.json',
    ])
    expect(await readFile(path.join(outDir, 'github-dark', `${key}.html`), 'utf8')).toContain(
      'data-theme="github-dark"',
    )
  })

  it('renders the fence body exactly as written', async () => {
    await write('a.md', fence('if (x)\n{\n\n    Run();\n}'))
    const render = fakeRenderer()

    await build({ render })

    expect(render.calls[0].code).toBe('if (x)\n{\n\n    Run();\n}')
  })

  it('writes a byte-identical index on a rebuild', async () => {
    await write('b.md', fence('var b = 2;'))
    await write('a.md', fence('var a = 1;'))

    await build()
    const first = await readFile(path.join(outDir, 'index.json'), 'utf8')
    const rebuild = await build()

    expect(rebuild.changed).toEqual([])
    expect(await readFile(path.join(outDir, 'index.json'), 'utf8')).toBe(first)
    expect(JSON.parse(first)).toMatchObject({ version: 1, fence: 'glosharp' })
  })

  it('reports unchanged artifacts as reused', async () => {
    await write('a.md', fence('var x = 42;'))

    await build()
    const second = await build()

    expect(second.changed).toEqual([])
    expect(second.unchanged).toHaveLength(2)
  })

  it('writes nothing in check mode but reports the drift', async () => {
    await write('a.md', fence('var x = 42;'))

    const result = await build({ check: true })

    expect(result.changed).toHaveLength(2)
    await expect(readdir(outDir)).rejects.toThrow()
  })

  it('detects a stale artifact as drift', async () => {
    await write('a.md', fence('var x = 42;'))
    await build()
    await write(
      path.join('out', 'github-dark', `${snippetKey('var x = 42;')}.html`),
      '<div>stale</div>',
    )

    const result = await build({ check: true })

    expect(result.changed).toContain(`github-dark/${snippetKey('var x = 42;')}.html`)
  })

  it('skips rendering when an artifact is already on disk', async () => {
    await write('a.md', fence('var x = 42;'))
    await build()
    const render = fakeRenderer()

    await build({ render, skipExisting: true })

    expect(render.calls).toEqual([])
  })

  it('reports artifacts no snippet claims, and prunes them on request', async () => {
    await write('a.md', fence('var x = 42;'))
    await build()
    await write(path.join('out', 'github-dark', `${'0'.repeat(64)}.html`), '<div>gone</div>')

    const reported = await build()
    expect(reported.orphaned).toEqual([`github-dark/${'0'.repeat(64)}.html`])

    const pruned = await build({ prune: true })
    expect(await readdir(path.join(outDir, 'github-dark'))).toEqual([
      `${snippetKey('var x = 42;')}.html`,
    ])
    expect(pruned.orphaned).toHaveLength(1)
  })

  it('never touches files in --out that are not Glo# artifacts', async () => {
    await write('a.md', fence('var x = 42;'))
    const foreign = [
      'out/blog/index.html',
      'out/assets/app.html',
      `out/assets/${'a'.repeat(64)}.html`,
      'out/github-dark/notes.html',
      'out/github-dark/README.md',
    ]
    for (const file of foreign) await write(file, 'keep me')

    const result = await build({ prune: true })

    expect(result.orphaned).toEqual([])
    for (const file of foreign) expect(await readFile(path.join(root, file), 'utf8')).toBe('keep me')
  })

  it('refuses to adopt an --out whose index.json is not ours', async () => {
    await write('a.md', fence('var x = 42;'))
    await write('out/index.json', '{"name":"my-site","version":"1.0.0"}')

    await expect(build({ prune: true })).rejects.toThrow(/not a Glo# artifact index/)
    expect(await readFile(path.join(outDir, 'index.json'), 'utf8')).toContain('my-site')
  })

  it('prunes theme directories listed in the previous index', async () => {
    await write('a.md', fence('var x = 42;'))
    await build({ themes: ['github-dark', 'dracula'] })

    const result = await build({ themes: ['github-dark'], prune: true })

    expect(result.pruned).toEqual([`dracula/${snippetKey('var x = 42;')}.html`])
  })

  it('renders a theme a fence pins, and does not orphan it', async () => {
    await write('a.md', fence('var x = 42;', 'theme="github-light"'))
    const render = fakeRenderer()

    const result = await build({ render, themes: ['github-dark'] })

    expect(render.calls.map((c) => c.theme).sort()).toEqual(['github-dark', 'github-light'])
    expect(result.orphaned).toEqual([])
    expect(result.snippets[0].occurrences[0].theme).toBe('github-light')
  })

  it('rejects a pinned theme that is not a plain name', async () => {
    await write('a.md', fence('var x = 42;', 'theme="../../etc"'))
    await expect(build()).rejects.toThrow(/a\.md:3: invalid theme/)
  })

  it('writes no index and prunes nothing when a render fails', async () => {
    await write('a.md', fence('var ok = 1;'))
    await build()
    await write(path.join('out', 'github-dark', `${'0'.repeat(64)}.html`), 'old')
    const before = await readFile(path.join(outDir, 'index.json'), 'utf8')
    await write('b.md', fence('var bad = 2;'))

    const result = await build({
      prune: true,
      render: async ({ code }) => {
        if (code.includes('bad')) throw new Error('unknown theme')
        return code
      },
    })

    expect(result.indexSkipped).toBe(true)
    expect(result.pruned).toEqual([])
    expect(await readFile(path.join(outDir, 'index.json'), 'utf8')).toBe(before)
  })

  it('reports unexpected compile errors once per snippet, not per theme', async () => {
    await write('a.md', fence('var x = 42;'))
    await write('b.md', fence('int y = "nope";'))
    const diagnosed: string[] = []

    const result = await build({
      themes: ['github-dark', 'github-light'],
      diagnose: async ({ code, occurrence }) => {
        diagnosed.push(code)
        return code.includes('nope') ? [`${occurrence.file}:${occurrence.line + 1} CS0029: Cannot convert`] : []
      },
    })

    expect(diagnosed.sort()).toEqual(['int y = "nope";', 'var x = 42;'])
    expect(result.compileErrors).toEqual([
      { key: snippetKey('int y = "nope";'), occurrences: [{ file: 'b.md', line: 3 }], errors: ['b.md:4 CS0029: Cannot convert'] },
    ])
  })

  it('treats a dropped theme directory as orphaned', async () => {
    await write('a.md', fence('var x = 42;'))
    await build({ themes: ['github-dark', 'github-light'] })

    const narrowed = await build({ themes: ['github-dark'] })

    expect(narrowed.orphaned).toEqual([`github-light/${snippetKey('var x = 42;')}.html`])
  })

  it('collects render failures instead of aborting the build', async () => {
    await write('a.md', fence('var ok = 1;'))
    await write('b.md', fence('var bad = 2;'))

    const result = await build({
      render: async ({ code, theme }) => {
        if (code.includes('bad')) throw new Error('CS1002: ; expected')
        return `<div data-theme="${theme}">${code}</div>`
      },
    })

    expect(result.failures).toEqual([
      { key: snippetKey('var bad = 2;'), theme: 'github-dark', message: 'CS1002: ; expected' },
    ])
    expect(result.changed).toContain(`github-dark/${snippetKey('var ok = 1;')}.html`)
  })

  it('renders each distinct snippet once per theme', async () => {
    await write('a.md', fence('var x = 42;'))
    await write('b.md', fence('var x = 42;'))
    const render = fakeRenderer()

    await build({ render, themes: ['github-dark', 'github-light'] })

    expect(render.calls).toHaveLength(2)
  })
})

describe('buildIndex', () => {
  it('sorts snippets, occurrences, and themes', () => {
    const index = buildIndex({
      fence: 'glosharp',
      themes: ['github-light', 'github-dark'],
      snippets: [
        { key: 'ff', code: 'b', occurrences: [{ file: 'z.md', line: 9 }] },
        {
          key: '00',
          code: 'a',
          framework: 'net10.0',
          occurrences: [
            { file: 'b.md', line: 2 },
            { file: 'a.md', line: 5 },
          ],
        },
      ],
    })

    expect(Object.keys(index.snippets)).toEqual(['00', 'ff'])
    expect(index.themes).toEqual(['github-dark', 'github-light'])
    expect(index.snippets['00'].occurrences.map((o) => o.file)).toEqual(['a.md', 'b.md'])
    expect(index.snippets['00'].framework).toBe('net10.0')
    expect(index.snippets.ff.framework).toBeUndefined()
  })
})

describe('collectMarkdownFiles', () => {
  it('walks directories for markdown and sorts the result', async () => {
    await write('b.md', '')
    await write('a/c.mdx', '')
    await write('a/notes.txt', '')
    await write('node_modules/skip.md', '')

    expect(await collectMarkdownFiles([root])).toEqual([
      path.join(root, 'a', 'c.mdx'),
      path.join(root, 'b.md'),
    ])
  })

  it('accepts explicit files regardless of extension', async () => {
    await write('page.markdown', '')

    expect(await collectMarkdownFiles([path.join(root, 'page.markdown')])).toEqual([
      path.join(root, 'page.markdown'),
    ])
  })

  it('deduplicates a file listed both directly and via its directory', async () => {
    await write('a.md', '')

    expect(await collectMarkdownFiles([root, path.join(root, 'a.md')])).toEqual([
      path.join(root, 'a.md'),
    ])
  })
})
