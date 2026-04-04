import { describe, it, expect, vi } from 'vitest'
import { createHash } from 'node:crypto'
import { codeToHtml } from 'shiki'
import { transformerGloSharpWithResult, transformerGloSharpFromMap, processGloSharpBlocks, type TransformerGloSharpOptions, type GloSharpResultMap } from '../src/index.js'
import type { GloSharpResult } from '@glosharp/core'

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
  completions: [],
  highlights: [],
  hidden: [],
  meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true },
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
  completions: [],
  highlights: [],
  hidden: [],
  meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: false },
}

describe('transformerGloSharpWithResult', () => {
  it('replaces code with processed result', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(sampleResult)],
    })

    // Should render the cleaned code, not the original with markers
    expect(html).not.toContain('^?')
    expect(html).toContain('var')
  })

  it('injects hover popup elements', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(sampleResult)],
    })

    expect(html).toContain('glosharp-hover')
    expect(html).toContain('glosharp-popup')
    expect(html).toContain('anchor-name')
    expect(html).toContain('position-anchor')
  })

  it('renders structured display parts', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(sampleResult)],
    })

    expect(html).toContain('glosharp-keyword')
    expect(html).toContain('glosharp-localName')
    expect(html).toContain('glosharp-popup-code')
  })

  it('injects error annotations for unexpected errors', async () => {
    const html = await codeToHtml(errorResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(errorResult)],
    })

    expect(html).toContain('glosharp-error-message')
    expect(html).toContain('CS0103')
  })

  it('skips expected errors', async () => {
    const expectedErrorResult: GloSharpResult = {
      ...errorResult,
      errors: [{ ...errorResult.errors[0], expected: true }],
    }

    const html = await codeToHtml(expectedErrorResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(expectedErrorResult)],
    })

    expect(html).not.toContain('glosharp-error-message')
  })
})

describe('completion list rendering', () => {
  const completionResult: GloSharpResult = {
    code: 'Console.',
    original: 'Console.\n//      ^|',
    lang: 'csharp',
    hovers: [],
    errors: [],
    completions: [{
      line: 0,
      character: 8,
      items: [
        { label: 'WriteLine', kind: 'Method', detail: 'void Console.WriteLine(string?)' },
        { label: 'Write', kind: 'Method', detail: 'void Console.Write(string?)' },
      ],
    }],
    highlights: [],
    hidden: [],
    meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true },
  }

  it('injects completion list for completion results', async () => {
    const html = await codeToHtml(completionResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(completionResult)],
    })

    expect(html).toContain('glosharp-completion-list')
    expect(html).toContain('glosharp-completion-item')
    expect(html).toContain('WriteLine')
  })

  it('renders kind badge and label for each item', async () => {
    const html = await codeToHtml(completionResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(completionResult)],
    })

    expect(html).toContain('glosharp-completion-kind-Method')
    expect(html).toContain('glosharp-completion-label')
    expect(html).toContain('glosharp-completion-kind')
  })

  it('does not inject completions when array is empty', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpWithResult(sampleResult)],
    })

    expect(html).not.toContain('glosharp-completion-list')
  })
})

describe('TransformerGloSharpOptions', () => {
  it('accepts project option in type', () => {
    const options: TransformerGloSharpOptions = {
      project: './MyProject.csproj',
    }
    expect(options.project).toBe('./MyProject.csproj')
  })

  it('accepts region option in type', () => {
    const options: TransformerGloSharpOptions = {
      region: 'getting-started',
    }
    expect(options.region).toBe('getting-started')
  })
})

// --- New tests for batch processing and result-map transformer ---

function hashCode(code: string): string {
  return createHash('sha256').update(code).digest('hex')
}

describe('transformerGloSharpFromMap', () => {
  it('replaces code and injects hovers when code is in map', async () => {
    const resultMap: GloSharpResultMap = new Map()
    resultMap.set(hashCode(sampleResult.original), sampleResult)

    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(resultMap)],
    })

    expect(html).not.toContain('^?')
    expect(html).toContain('glosharp-hover')
    expect(html).toContain('glosharp-popup')
    expect(html).toContain('anchor-name')
  })

  it('is a no-op when code is not in map', async () => {
    const resultMap: GloSharpResultMap = new Map()

    const code = 'var y = 100;'
    const html = await codeToHtml(code, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(resultMap)],
    })

    // Should render normally without any glosharp elements
    expect(html).not.toContain('glosharp-hover')
    expect(html).not.toContain('glosharp-popup')
    expect(html).not.toContain('glosharp-error-message')
    expect(html).toContain('var')
  })

  it('handles multiple sequential codeToHtml calls correctly', async () => {
    const secondResult: GloSharpResult = {
      ...errorResult,
    }

    const resultMap: GloSharpResultMap = new Map()
    resultMap.set(hashCode(sampleResult.original), sampleResult)
    resultMap.set(hashCode(errorResult.original), secondResult)

    const transformer = transformerGloSharpFromMap(resultMap)

    // First call: hover result
    const html1 = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformer],
    })
    expect(html1).toContain('glosharp-hover')
    expect(html1).not.toContain('glosharp-error-message')

    // Second call: error result
    const html2 = await codeToHtml(errorResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformer],
    })
    expect(html2).toContain('glosharp-error-message')
    expect(html2).toContain('CS0103')

    // Third call: code not in map — no-op
    const html3 = await codeToHtml('var z = 0;', {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformer],
    })
    expect(html3).not.toContain('glosharp-hover')
    expect(html3).not.toContain('glosharp-error-message')
  })

  it('has name property set to glosharp', () => {
    const resultMap: GloSharpResultMap = new Map()
    const transformer = transformerGloSharpFromMap(resultMap)
    expect(transformer.name).toBe('glosharp')
  })
})

describe('processGloSharpBlocks', () => {
  // Mock the glosharp bridge to avoid needing the real CLI
  vi.mock('glosharp', () => ({
    createGloSharp: vi.fn(() => {
      const cache = new Map<string, GloSharpResult>()
      let callCount = 0
      return {
        process: vi.fn(async (opts: { code: string; project?: string; region?: string }): Promise<GloSharpResult> => {
          const cacheKey = createHash('sha256').update(opts.code).digest('hex')
          if (cache.has(cacheKey)) return cache.get(cacheKey)!

          callCount++
          const result: GloSharpResult = {
            code: opts.code.replace(/\/\/\s*\^[?|].*/g, '').trim(),
            original: opts.code,
            lang: 'csharp',
            hovers: [{
              line: 0,
              character: 4,
              length: 1,
              text: `(local variable) int x [call ${callCount}]`,
              parts: [{ kind: 'keyword', text: 'int' }],
              docs: null,
              symbolKind: 'Local',
              targetText: 'x',
            }],
            errors: [],
            completions: [],
            highlights: [],
            hidden: [],
            meta: {
              targetFramework: opts.project ?? 'net8.0',
              packages: [],
              compileSucceeded: true,
            },
          }
          cache.set(cacheKey, result)
          return result
        }),
        _getCallCount: () => callCount,
      }
    }),
  }))

  it('returns map with correct entries for blocks with markers', async () => {
    const blocks = [
      'var x = 42;\n//  ^?',
      'var y = 100;\n//   ^?',
    ]

    const resultMap = await processGloSharpBlocks(blocks)

    expect(resultMap.size).toBe(2)
    expect(resultMap.has(hashCode(blocks[0]))).toBe(true)
    expect(resultMap.has(hashCode(blocks[1]))).toBe(true)
  })

  it('skips blocks without markers', async () => {
    const blocks = [
      'var x = 42;',           // no markers
      'var y = 100;\n//   ^?', // has markers
    ]

    const resultMap = await processGloSharpBlocks(blocks)

    expect(resultMap.size).toBe(1)
    expect(resultMap.has(hashCode(blocks[0]))).toBe(false)
    expect(resultMap.has(hashCode(blocks[1]))).toBe(true)
  })

  it('returns empty map for empty input', async () => {
    const resultMap = await processGloSharpBlocks([])
    expect(resultMap.size).toBe(0)
  })

  it('returns empty map when no blocks have markers', async () => {
    const resultMap = await processGloSharpBlocks(['var x = 42;', 'var y = 100;'])
    expect(resultMap.size).toBe(0)
  })

  it('passes per-block project/region overrides', async () => {
    const blocks = [
      { code: 'var x = 42;\n//  ^?', project: './A.csproj' },
      { code: 'var y = 100;\n//   ^?', region: 'intro' },
    ]

    const resultMap = await processGloSharpBlocks(blocks, { project: './B.csproj' })

    // First block uses its own project
    const result1 = resultMap.get(hashCode(blocks[0].code))!
    expect(result1.meta.targetFramework).toBe('./A.csproj')

    // Second block falls back to shared project
    const result2 = resultMap.get(hashCode(blocks[1].code))!
    expect(result2.meta.targetFramework).toBe('./B.csproj')
  })

  it('deduplicates identical code blocks via cache', async () => {
    const code = 'var x = 42;\n//  ^?'
    const blocks = [code, code, code]

    const resultMap = await processGloSharpBlocks(blocks)

    // Should only have one entry (all three are identical)
    expect(resultMap.size).toBe(1)

    // The result should be the same for all (call count embedded in text)
    const result = resultMap.get(hashCode(code))!
    expect(result.hovers[0].text).toContain('call 1')
  })
})
