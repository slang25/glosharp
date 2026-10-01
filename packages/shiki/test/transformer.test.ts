import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createHash } from 'node:crypto'
import { codeToHtml } from 'shiki'
import { transformerNotationHighlight, transformerMetaHighlight } from '@shikijs/transformers'
import {
  transformerGloSharpWithResult,
  transformerGloSharpFromMap,
  processGloSharpBlocks,
  filterCompletions,
  snippetKey,
  type TransformerGloSharpOptions,
  type GloSharpResultMap,
} from '../src/index.js'
import type { GloSharpResult } from '@glosharp/core'
import { calls } from './fake-core.js'

// The bridge is mocked: these tests never start the real CLI.
vi.mock('@glosharp/core', async (importOriginal) => {
  const actual = await importOriginal<typeof import('@glosharp/core')>()
  const fake = await import('./fake-core.js')
  return {
    ...actual,
    createGloSharp: vi.fn(() => {
      // Mirror the real bridge's in-memory cache so dedupe is observable.
      const cache = new Map<string, Promise<GloSharpResult>>()
      return {
        process: (opts: Parameters<typeof fake.fakeProcess>[0]) => {
          const key = JSON.stringify(opts)
          if (!cache.has(key)) cache.set(key, fake.fakeProcess(opts))
          return cache.get(key)!
        },
        render: vi.fn(),
        clearCache: vi.fn(),
      }
    }),
  }
})

beforeEach(() => {
  calls.length = 0
})

const sampleResult: GloSharpResult = {
  code: 'var x = 42;\nConsole.WriteLine(x);',
  original: 'var x = 42;\n//  ^?\nConsole.WriteLine(x);\n//                ^?',
  lang: 'csharp',
  hovers: [
    {
      line: 0,
      character: 4,
      length: 1,
      text: '(local variable) int x',
      parts: [
        { kind: 'punctuation', text: '(' },
        { kind: 'text', text: 'local variable' },
        { kind: 'punctuation', text: ')' },
        { kind: 'space', text: ' ' },
        { kind: 'keyword', text: 'int' },
        { kind: 'space', text: ' ' },
        { kind: 'localName', text: 'x' },
      ],
      docs: null,
      symbolKind: 'Local',
      targetText: 'x',
    },
  ],
  errors: [],
  hiddenErrors: [],
  completions: [],
  highlights: [],
  hidden: [],
  tags: [],
  meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true, warnings: [] },
}

const errorResult: GloSharpResult = {
  code: 'Console.WriteLine(undeclared);',
  original: 'Console.WriteLine(undeclared);',
  lang: 'csharp',
  hovers: [],
  errors: [
    {
      line: 0,
      character: 18,
      length: 10,
      code: 'CS0103',
      message: "The name 'undeclared' does not exist in the current context",
      severity: 'error',
      expected: false,
    },
  ],
  hiddenErrors: [],
  completions: [],
  highlights: [],
  hidden: [],
  tags: [],
  meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: false, warnings: [] },
}

const render = (result: GloSharpResult, extra: Parameters<typeof codeToHtml>[1]['transformers'] = []) =>
  codeToHtml(result.original, {
    lang: 'csharp',
    theme: 'github-dark',
    transformers: [...extra, transformerGloSharpWithResult(result)],
  })

describe('transformerGloSharpWithResult', () => {
  it('replaces code with processed result', async () => {
    const html = await render(sampleResult)
    expect(html).not.toContain('^?')
    expect(html).toContain('var')
  })

  it('injects hover popup elements with anchor positioning', async () => {
    const html = await render(sampleResult)
    expect(html).toContain('glosharp-hover')
    expect(html).toContain('glosharp-popup')
    expect(html).toMatch(/anchor-name:--gs[a-z0-9]+-0/)
    expect(html).toMatch(/position-anchor:--gs[a-z0-9]+-0/)
  })

  it('renders structured display parts', async () => {
    const html = await render(sampleResult)
    expect(html).toContain('glosharp-keyword')
    expect(html).toContain('glosharp-localName')
    expect(html).toContain('glosharp-popup-code')
  })

  it('makes hover targets keyboard-focusable and describes them', async () => {
    const html = await render(sampleResult)
    const id = /role="tooltip" id="([^"]+)"/.exec(html)?.[1]
    expect(id).toBeTruthy()
    expect(html).toContain(`tabindex="0" aria-describedby="${id}"`)
  })

  it('can turn focusability off', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(sampleResult, { focusable: false })],
    })
    expect(html).not.toContain('aria-describedby')
    expect(html.match(/tabindex="0"/g)).toHaveLength(1) // Shiki's own <pre tabindex="0">
  })

  it('produces identical HTML on every render (deterministic anchors)', async () => {
    expect(await render(sampleResult)).toBe(await render(sampleResult))
  })

  it('injects error underline and message for unexpected errors', async () => {
    const html = await render(errorResult)
    expect(html).toContain('glosharp-error-message glosharp-severity-error')
    expect(html).toMatch(/<span class="glosharp-error-underline glosharp-severity-error">[^]*?undeclared/)
    expect(html).toContain('CS0103')
    expect(html).toContain('href="https://msdn.microsoft.com/query/roslyn.query?appId=roslyn&#x26;k=k(CS0103)"')
  })

  it('renders expected (@errors) diagnostics too, marked as expected', async () => {
    const html = await render({ ...errorResult, errors: [{ ...errorResult.errors[0], expected: true }] })
    expect(html).toContain('glosharp-error-message glosharp-severity-error glosharp-error-expected')
  })

  it('puts annotation blocks after the line break, so no blank row follows them', async () => {
    const twoLines: GloSharpResult = {
      ...errorResult,
      code: 'Console.WriteLine(undeclared);\nvar y = 1;',
      original: 'Console.WriteLine(undeclared);\nvar y = 1;',
    }
    const html = await render(twoLines)
    // line 0 </span>, newline, the message block, then line 1
    expect(html).toMatch(/<\/span>\n<span class="glosharp-error-message[^>]*>.*?<\/span><span class="line">/)
  })

  it('renders persistent ^? hovers as an always-visible query box', async () => {
    const html = await render({ ...sampleResult, hovers: [{ ...sampleResult.hovers[0], persistent: true }] })
    expect(html).toContain('glosharp-hover glosharp-hover-persistent')
    expect(html).toMatch(/<span class="glosharp-query" style="--glosharp-col:4"><span class="glosharp-query-box" id="([^"]+)" role="note">/)
  })

  it('keeps the persistent class when the hover covers part of a token', async () => {
    const result: GloSharpResult = {
      ...sampleResult,
      code: 'global::System.Console.WriteLine();',
      original: 'global::System.Console.WriteLine();',
      hovers: [{ ...sampleResult.hovers[0], line: 0, character: 15, length: 7, targetText: 'Console', persistent: true }],
    }
    const html = await render(result)
    expect(html).toMatch(/glosharp-hover-persistent[^>]*>(<span[^>]*>)?Console/)
  })

  it('renders highlight, focus and diff line annotations', async () => {
    const result: GloSharpResult = {
      ...sampleResult,
      code: 'var a = 1;\nvar b = 2;\nvar c = 3;\nvar d = 4;',
      original: 'var a = 1;\nvar b = 2;\nvar c = 3;\nvar d = 4;',
      hovers: [],
      highlights: [
        { line: 0, character: 0, length: 10, kind: 'highlight' },
        { line: 1, character: 0, length: 10, kind: 'add' },
        { line: 2, character: 0, length: 10, kind: 'remove' },
        { line: 3, character: 0, length: 10, kind: 'focus' },
      ],
    }
    const html = await render(result)
    expect(html).toContain('class="line glosharp-highlight glosharp-focus-dim"')
    expect(html).toContain('class="line glosharp-diff-add glosharp-focus-dim"')
    expect(html).toContain('class="line glosharp-diff-remove glosharp-focus-dim"')
    expect(html).toContain('class="line glosharp-focused"')
    expect(html).toMatch(/<pre class="[^"]*glosharp-has-focus/)
  })

  it('renders custom tags after their line', async () => {
    const result: GloSharpResult = {
      ...sampleResult,
      tags: [
        { name: 'log', text: 'cached', line: 0 },
        { name: 'warn', text: 'allocates', line: 1 },
      ],
    }
    const html = await render(result)
    expect(html).toContain('<span class="glosharp-tag glosharp-tag-log" role="note"><span class="glosharp-tag-name">log</span>cached</span>')
    expect(html).toContain('glosharp-tag glosharp-tag-warn')
  })

  it('marks the <pre> with the theme kind and dual-theme support', async () => {
    const dark = await render(sampleResult)
    expect(dark).toMatch(/<pre class="shiki github-dark glosharp glosharp-theme-dark"/)
    expect(dark).toMatch(/--glosharp-bg:#24292e/)

    const dual = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      themes: { light: 'github-light', dark: 'github-dark' },
      transformers: [transformerGloSharpWithResult(sampleResult)],
    })
    expect(dual).toMatch(/class="[^"]*glosharp-theme-light glosharp-dual/)
  })
})

describe('interop with other Shiki transformers', () => {
  // Line transformers turn `class="line"` into "line highlighted" (or an
  // array); hovers must still land on the right lines.
  const result: GloSharpResult = {
    ...sampleResult,
    code: 'var a = 1;\nvar b = 2;\nvar c = 3;',
    original: 'var a = 1;\nvar b = 2; // [!code highlight]\nvar c = 3;',
    hovers: [0, 1, 2].map((line) => ({ ...sampleResult.hovers[0], line, character: 4, length: 1, targetText: 'abc'[line] })),
    errors: [{ ...errorResult.errors[0], line: 2, character: 8, length: 1 }],
  }

  it('places hovers and errors by line index with notation transformers present', async () => {
    const html = await codeToHtml(result.original, {
      lang: 'csharp',
      theme: 'github-dark',
      meta: { __raw: '{1}' },
      transformers: [transformerMetaHighlight(), transformerNotationHighlight(), transformerGloSharpWithResult(result)],
    })
    const lines = html.split('\n')
    expect(lines[0]).toMatch(/class="line highlighted"[\s\S]*glosharp-hover[\s\S]*>a</)
    expect(lines[1]).toMatch(/glosharp-hover[\s\S]*>b</)
    expect(lines[2]).toMatch(/glosharp-hover[\s\S]*>c</)
    expect(html.match(/class="glosharp-hover"/g)).toHaveLength(3)
    expect(lines[2]).toContain('glosharp-error-underline')
    expect(lines[0]).not.toContain('glosharp-error-underline')
  })
})

describe('completion list rendering', () => {
  const completionResult: GloSharpResult = {
    ...sampleResult,
    code: 'list.Ad',
    original: 'list.Ad\n//     ^|',
    hovers: [],
    completions: [
      {
        line: 0,
        character: 7,
        items: [
          { label: 'Add', kind: 'Method', detail: 'void List<int>.Add(int item)' },
          { label: 'AddRange', kind: 'Method', detail: null },
          { label: 'AddRange', kind: 'Method', detail: null },
          { label: 'All', kind: 'ExtensionMethod', detail: null },
          { label: 'for', kind: 'Snippet', detail: null },
        ],
      },
    ],
  }

  it('renders a styled list filtered to the typed prefix, without duplicates', async () => {
    const html = await render(completionResult)
    expect(html).toContain('glosharp-completion-list')
    expect(html.match(/glosharp-completion-item /g)).toHaveLength(2)
    expect(html).toContain('glosharp-completion-kind-Method')
    expect(html).toContain('<span class="glosharp-completion-match">Ad</span>d')
    expect(html).not.toContain('>All<')
  })

  it('does not inject completions when array is empty', async () => {
    const html = await render(sampleResult)
    expect(html).not.toContain('glosharp-completion-list')
  })

  it('filterCompletions keeps everything when nothing matches the prefix', () => {
    const items = [{ label: 'Foo', kind: 'Method', detail: null }]
    expect(filterCompletions(items, 'Zz')).toEqual(items)
    expect(filterCompletions(items, '')).toEqual(items)
  })
})

describe('TransformerGloSharpOptions', () => {
  it('accepts project and region options', () => {
    const options: TransformerGloSharpOptions = { project: './MyProject.csproj', region: 'getting-started' }
    expect(options.project).toBe('./MyProject.csproj')
  })
})

describe('transformerGloSharpFromMap', () => {
  it('replaces code and injects hovers when code is in map', async () => {
    const resultMap: GloSharpResultMap = new Map()
    resultMap.set(snippetKey(sampleResult.original), sampleResult)

    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(resultMap)],
    })

    expect(html).not.toContain('^?')
    expect(html).toContain('glosharp-hover')
  })

  it('matches regardless of a trailing newline or CRLF on either side', async () => {
    const resultMap: GloSharpResultMap = new Map()
    resultMap.set(snippetKey(`${sampleResult.original}\n`), sampleResult)

    const html = await codeToHtml(sampleResult.original.replace(/\n/g, '\r\n'), {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(resultMap)],
    })
    expect(html).toContain('glosharp-hover')
  })

  it('still finds maps keyed by a raw sha256 of the code', async () => {
    const resultMap: GloSharpResultMap = new Map()
    resultMap.set(createHash('sha256').update(sampleResult.original).digest('hex'), sampleResult)
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(resultMap)],
    })
    expect(html).toContain('glosharp-hover')
  })

  it('is a no-op when code is not in map', async () => {
    const html = await codeToHtml('var y = 100;', {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(new Map())],
    })
    expect(html).not.toContain('glosharp-')
    expect(html).toContain('var')
  })

  it('handles concurrent codeToHtml calls with one transformer', async () => {
    const resultMap: GloSharpResultMap = new Map()
    resultMap.set(snippetKey(sampleResult.original), sampleResult)
    resultMap.set(snippetKey(errorResult.original), errorResult)
    const transformer = transformerGloSharpFromMap(resultMap)
    const opts = { lang: 'csharp', theme: 'github-dark', transformers: [transformer] }

    const [html1, html2, html3] = await Promise.all([
      codeToHtml(sampleResult.original, opts),
      codeToHtml(errorResult.original, opts),
      codeToHtml('var z = 0;', opts),
    ])
    expect(html1).toContain('glosharp-hover')
    expect(html1).not.toContain('glosharp-error-message')
    expect(html2).toContain('CS0103')
    expect(html3).not.toContain('glosharp-')
  })

  it('has name property set to glosharp', () => {
    expect(transformerGloSharpFromMap(new Map()).name).toBe('glosharp')
  })
})

describe('processGloSharpBlocks', () => {
  it('returns map entries for blocks with markers, keyed by snippetKey', async () => {
    const blocks = ['var x = 42;\n//  ^?', 'var y = 100;\n//   ^?']
    const resultMap = await processGloSharpBlocks(blocks)

    expect(resultMap.size).toBe(2)
    expect(resultMap.has(snippetKey(blocks[0]))).toBe(true)
    expect(resultMap.has(snippetKey(blocks[1]))).toBe(true)
  })

  it('skips blocks without markers (batch-processing spec)', async () => {
    const resultMap = await processGloSharpBlocks(['var x = 42;', 'Console.WriteLine(x);\n// ^?'])
    expect(resultMap.size).toBe(1)
    expect(calls).toHaveLength(1)
  })

  it('processes unmarked blocks with processUnmarked or force', async () => {
    expect((await processGloSharpBlocks(['var x = 42;', 'var y = 1;'], { processUnmarked: true })).size).toBe(2)
    expect((await processGloSharpBlocks([{ code: 'var x = 42;', force: true }])).size).toBe(1)
  })

  it('returns empty map for empty input', async () => {
    expect((await processGloSharpBlocks([])).size).toBe(0)
  })

  it('passes shared and per-block options to the bridge', async () => {
    await processGloSharpBlocks(
      [{ code: 'var a = 1;\n// ^?', project: './A.csproj', region: 'intro' }, 'var b = 1;\n// ^?'],
      { project: './B.csproj', framework: 'net9.0' },
    )
    expect(calls).toEqual([
      expect.objectContaining({ project: './A.csproj', region: 'intro', framework: 'net9.0' }),
      expect.objectContaining({ project: './B.csproj', framework: 'net9.0' }),
    ])
  })

  it('keeps identical code with different per-block options apart', async () => {
    const code = 'var x = 42;\n//  ^?'
    const resultMap = await processGloSharpBlocks([
      { code, project: './A.csproj' },
      { code, project: './B.csproj' },
    ])
    expect(resultMap.size).toBe(2)
    expect(resultMap.get(snippetKey(code, { project: './A.csproj' }))).toBeDefined()
    expect(resultMap.get(snippetKey(code, { project: './B.csproj' }))).toBeDefined()
  })

  it('records shared options so transformerGloSharpFromMap finds the entries', async () => {
    const code = 'var x = 42;\n//  ^?'
    const resultMap = await processGloSharpBlocks([code], { project: './A.csproj' })
    const html = await codeToHtml(code, { lang: 'csharp', theme: 'github-dark', transformers: [transformerGloSharpFromMap(resultMap)] })
    expect(html).toContain('glosharp-hover')
  })

  it('deduplicates identical code blocks', async () => {
    const code = 'var x = 42;\n//  ^?'
    const resultMap = await processGloSharpBlocks([code, code, code])
    expect(resultMap.size).toBe(1)
    expect(calls).toHaveLength(1)
  })

  it('names the failing block when the CLI fails', async () => {
    await expect(processGloSharpBlocks(['var ok = 1;\n// ^?', 'var CRASH = 1;\n// ^?'])).rejects.toThrow(
      /block 1 \(var CRASH = 1;\): glosharp exited with code 1/,
    )
  })

  it("leaves failing blocks out with onCliError: 'warn'", async () => {
    const warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
    const map = await processGloSharpBlocks(['var ok = 1;\n// ^?', 'var CRASH = 1;\n// ^?'], { onCliError: 'warn' })
    expect(map.size).toBe(1)
    expect(warn).toHaveBeenCalledWith(expect.stringContaining('block 1'))
    warn.mockRestore()
  })
})
