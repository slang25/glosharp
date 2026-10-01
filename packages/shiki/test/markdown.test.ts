import { describe, it, expect, vi, beforeEach, afterEach } from 'vitest'
import { unified } from 'unified'
import remarkParse from 'remark-parse'
import remarkRehype from 'remark-rehype'
import rehypeStringify from 'rehype-stringify'
import { compile } from '@mdx-js/mdx'
import { codeToHtml } from 'shiki'
import { remarkGloSharp, satteriGloSharp, transformerGloSharp, selectBlock, type GloSharpMarkdownOptions } from '../src/index.js'
import { calls } from './fake-core.js'

vi.mock('@glosharp/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@glosharp/core')>()
  const fake = await import('./fake-core.js')
  return {
    ...actual,
    createGloSharp: vi.fn(() => ({ process: fake.fakeProcess, render: vi.fn(), clearCache: vi.fn() })),
  }
})

let warnings: string[]
const logger = { warn: (message: string) => warnings.push(message) }

beforeEach(() => {
  calls.length = 0
  warnings = []
})

async function toHtml(markdown: string, options: GloSharpMarkdownOptions = {}, path = 'docs/page.md') {
  const file = await unified()
    .use(remarkParse)
    .use(remarkGloSharp, { render: 'html', logger, ...options })
    .use(remarkRehype)
    .use(rehypeStringify)
    .process({ value: markdown, path })
  return String(file)
}

const fence = (meta: string, code: string, lang = 'csharp') => `\`\`\`${lang}${meta ? ` ${meta}` : ''}\n${code}\n\`\`\`\n`

describe('selectBlock', () => {
  it('selects C# blocks with markers, or opted in with `glosharp`', () => {
    expect(selectBlock({ lang: 'csharp', value: 'var x = 1;\n// ^?' })).toBeDefined()
    expect(selectBlock({ lang: 'cs', meta: 'glosharp', value: 'var x = 1;' })).toBeDefined()
    expect(selectBlock({ lang: 'csharp', value: 'var x = 1;' })).toBeUndefined()
    expect(selectBlock({ lang: 'ts', value: 'const x = 1 // ^?' })).toBeUndefined()
  })

  it('honours `no-glosharp`', () => {
    expect(selectBlock({ lang: 'csharp', meta: 'title="x" no-glosharp', value: 'var x = 1;\n// ^?' })).toBeUndefined()
  })

  it('with explicitTrigger, only opted-in blocks are selected', () => {
    expect(selectBlock({ lang: 'csharp', value: 'var x = 1;\n// ^?' }, { explicitTrigger: true })).toBeUndefined()
    expect(selectBlock({ lang: 'csharp', meta: 'glosharp', value: 'var x = 1;' }, { explicitTrigger: true })).toBeDefined()
    expect(selectBlock({ lang: 'csharp', meta: 'twoslash', value: 'x' }, { explicitTrigger: /\btwoslash\b/ })).toBeDefined()
  })

  it('processUnmarked selects every C# block', () => {
    expect(selectBlock({ lang: 'csharp', value: 'var x = 1;' }, { processUnmarked: true })).toBeDefined()
  })

  it('treats a `glosharp` fence as an opt-in C# block', () => {
    expect(selectBlock({ lang: 'glosharp', value: 'var x = 1;' })).toMatchObject({ lang: 'csharp' })
  })

  it('reads region= and framework= from the fence meta into the key options', () => {
    expect(selectBlock({ lang: 'csharp', meta: 'glosharp region=intro framework="net10.0"', value: 'x' })?.effective).toEqual({
      framework: 'net10.0',
      region: 'intro',
    })
  })
})

describe('remarkGloSharp (render: html)', () => {
  it('renders marked blocks with Shiki and leaves the rest alone', async () => {
    const html = await toHtml(
      fence('', 'var answer = 42;\n//  ^?') + fence('', 'public void Foo() { }') + fence('no-glosharp', 'var skip = 1;\n// ^?'),
    )
    expect(html).toContain('<div class="glosharp-block"><pre class="shiki shiki-themes github-light github-dark glosharp')
    expect(html).toContain('glosharp-hover')
    expect(html.match(/glosharp-block/g)).toHaveLength(1)
    expect(html).toContain('<code class="language-csharp">public void Foo() { }')
    expect(html).toContain('// ^?') // the opted-out block keeps its marker text
    expect(calls).toHaveLength(1)
  })

  it('passes options and fence meta to the bridge', async () => {
    await toHtml(fence('glosharp region=demo', 'var x = 1;'), { project: './Docs.csproj' })
    expect(calls[0]).toMatchObject({ code: 'var x = 1;', project: './Docs.csproj', region: 'demo' })
  })

  it('warns about unexpected compile errors with file and line', async () => {
    const html = await toHtml(`# Title\n\n${fence('', 'var x = 1;\nBROKEN;\n// ^?')}`)
    expect(html).toContain('glosharp-error-message')
    expect(warnings).toHaveLength(1)
    // fence on line 3, the error on the snippet's second line → file line 5
    expect(warnings[0]).toContain('docs/page.md:3: snippet has unexpected compile errors')
    expect(warnings[0]).toContain('docs/page.md:5:5 CS0103')
  })

  it("throws on unexpected compile errors with onCompileError: 'throw'", async () => {
    await expect(toHtml(fence('', 'BROKEN;\n// ^?'), { onCompileError: 'throw' })).rejects.toThrow(/docs\/page\.md:1/)
  })

  it("stays quiet with onCompileError: 'ignore'", async () => {
    await toHtml(fence('', 'BROKEN;\n// ^?'), { onCompileError: 'ignore' })
    expect(warnings).toHaveLength(0)
  })

  it('surfaces meta.warnings', async () => {
    await toHtml(fence('', 'var WARNME = 1;\n// ^?'))
    expect(warnings[0]).toContain('caret points past the end of line 1')
  })

  it('fails the build when the CLI fails, naming the block', async () => {
    await expect(toHtml(`text\n\n${fence('', 'var CRASH = 1;\n// ^?')}`)).rejects.toThrow(
      /docs\/page\.md:3: glosharp exited with code 1/,
    )
  })

  it("downgrades CLI failures to one warning with onCliError: 'warn'", async () => {
    const html = await toHtml(fence('', 'var MISSING = 1;\n// ^?') + fence('', 'var MISSING = 2;\n// ^?'), {
      onCliError: 'warn',
    })
    expect(html).not.toContain('glosharp-block')
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('glosharp CLI not found')
  })
})

describe('remarkGloSharp with MDX', () => {
  it('emits a JSX element so MDX does not re-render the <pre>', async () => {
    const js = String(
      await compile(fence('', 'var answer = 42;\n//  ^?'), { remarkPlugins: [[remarkGloSharp, { logger }]] }),
    )
    expect(js).toContain('dangerouslySetInnerHTML')
    expect(js).toContain('glosharp-hover')
    expect(js).toContain('"glosharp-block"')
  })
})

describe('remarkGloSharp (render: transformer) + transformerGloSharp', () => {
  it('tags the block with its key and lets the site Shiki pass apply the result', async () => {
    const processor = unified().use(remarkParse).use(remarkGloSharp, { render: 'transformer', logger })
    const tree = await processor.run(processor.parse(fence('title="A.cs"', 'var answer = 42;\n//  ^?')))
    const code = (tree as unknown as { children: Array<{ value: string; meta: string }> }).children[0]

    expect(code.meta).toMatch(/^title="A\.cs" glosharp-key=[0-9a-f]{64}$/)
    expect(code.value).toContain('^?') // untouched; the transformer swaps in the processed code

    const html = await codeToHtml(code.value, {
      lang: 'csharp',
      theme: 'github-dark',
      meta: { __raw: code.meta },
      transformers: [transformerGloSharp()],
    })
    expect(html).toContain('glosharp-hover')
    expect(html).not.toContain('^?')
  })

  it('falls back to matching by code when the pipeline drops meta', async () => {
    const processor = unified().use(remarkParse).use(remarkGloSharp, { render: 'transformer', logger })
    await processor.run(processor.parse(fence('', 'var other = 7;\n//  ^?')))
    const html = await codeToHtml('var other = 7;\n//  ^?\n', {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharp()],
    })
    expect(html).toContain('glosharp-hover')
  })
})

describe('satteriGloSharp', () => {
  let original: string | undefined
  beforeEach(() => {
    original = process.cwd()
  })
  afterEach(() => {
    expect(process.cwd()).toBe(original)
  })

  it('processes code nodes and tags their meta for transformerGloSharp', async () => {
    const plugin = satteriGloSharp({ logger })
    const set: Array<[string, unknown]> = []
    const ctx = {
      fileURL: new URL(`file://${process.cwd()}/src/content/post.md`),
      setProperty: (_node: unknown, key: string, value: unknown) => set.push([key, value]),
    }

    await plugin.code({ lang: 'glosharp', meta: null, value: 'var s = 1;', position: { start: { line: 4 } } }, ctx)
    await plugin.code({ lang: 'csharp', meta: null, value: 'var plain = 1;' }, ctx)

    expect(plugin.name).toBe('glosharp')
    expect(set).toEqual([
      ['meta', expect.stringMatching(/^glosharp-key=[0-9a-f]{64}$/)],
      ['lang', 'csharp'],
    ])
    expect(calls).toHaveLength(1)
  })

  it('reports compile errors with the file path from the fileURL', async () => {
    const plugin = satteriGloSharp({ logger })
    const ctx = { fileURL: new URL(`file://${process.cwd()}/src/post.md`), setProperty: () => {} }
    await plugin.code({ lang: 'csharp', meta: 'glosharp', value: 'x;\nBROKEN;', position: { start: { line: 10 } } }, ctx)
    expect(warnings[0]).toContain('src/post.md:10')
    expect(warnings[0]).toContain('src/post.md:12:5 CS0103')
  })
})
