import { describe, it, expect, vi } from 'vitest'
import { createHash } from 'node:crypto'
import { codeToHtml } from 'shiki'
import { transformerTwohashWithResult, transformerTwohashFromMap, processTwohashBlocks, type TransformerTwohashOptions, type TwohashResultMap } from '../src/index.js'
import type { TwohashResult } from 'twohash'

const sampleResult: TwohashResult = {
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

const errorResult: TwohashResult = {
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

describe('transformerTwohashWithResult', () => {
  it('replaces code with processed result', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(sampleResult)],
    })

    // Should render the cleaned code, not the original with markers
    expect(html).not.toContain('^?')
    expect(html).toContain('var')
  })

  it('injects hover popup elements', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(sampleResult)],
    })

    expect(html).toContain('twohash-hover')
    expect(html).toContain('twohash-popup')
    expect(html).toContain('anchor-name')
    expect(html).toContain('position-anchor')
  })

  it('renders structured display parts', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(sampleResult)],
    })

    expect(html).toContain('twohash-keyword')
    expect(html).toContain('twohash-localName')
    expect(html).toContain('twohash-popup-code')
  })

  it('injects error annotations for unexpected errors', async () => {
    const html = await codeToHtml(errorResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(errorResult)],
    })

    expect(html).toContain('twohash-error-message')
    expect(html).toContain('CS0103')
  })

  it('skips expected errors', async () => {
    const expectedErrorResult: TwohashResult = {
      ...errorResult,
      errors: [{ ...errorResult.errors[0], expected: true }],
    }

    const html = await codeToHtml(expectedErrorResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(expectedErrorResult)],
    })

    expect(html).not.toContain('twohash-error-message')
  })
})

describe('completion list rendering', () => {
  const completionResult: TwohashResult = {
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
      transformers: [transformerTwohashWithResult(completionResult)],
    })

    expect(html).toContain('twohash-completion-list')
    expect(html).toContain('twohash-completion-item')
    expect(html).toContain('WriteLine')
  })

  it('renders kind badge and label for each item', async () => {
    const html = await codeToHtml(completionResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(completionResult)],
    })

    expect(html).toContain('twohash-completion-kind-Method')
    expect(html).toContain('twohash-completion-label')
    expect(html).toContain('twohash-completion-kind')
  })

  it('does not inject completions when array is empty', async () => {
    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashWithResult(sampleResult)],
    })

    expect(html).not.toContain('twohash-completion-list')
  })
})

describe('TransformerTwohashOptions', () => {
  it('accepts project option in type', () => {
    const options: TransformerTwohashOptions = {
      project: './MyProject.csproj',
    }
    expect(options.project).toBe('./MyProject.csproj')
  })

  it('accepts region option in type', () => {
    const options: TransformerTwohashOptions = {
      region: 'getting-started',
    }
    expect(options.region).toBe('getting-started')
  })
})

// --- New tests for batch processing and result-map transformer ---

function hashCode(code: string): string {
  return createHash('sha256').update(code).digest('hex')
}

describe('transformerTwohashFromMap', () => {
  it('replaces code and injects hovers when code is in map', async () => {
    const resultMap: TwohashResultMap = new Map()
    resultMap.set(hashCode(sampleResult.original), sampleResult)

    const html = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashFromMap(resultMap)],
    })

    expect(html).not.toContain('^?')
    expect(html).toContain('twohash-hover')
    expect(html).toContain('twohash-popup')
    expect(html).toContain('anchor-name')
  })

  it('is a no-op when code is not in map', async () => {
    const resultMap: TwohashResultMap = new Map()

    const code = 'var y = 100;'
    const html = await codeToHtml(code, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerTwohashFromMap(resultMap)],
    })

    // Should render normally without any twohash elements
    expect(html).not.toContain('twohash-hover')
    expect(html).not.toContain('twohash-popup')
    expect(html).not.toContain('twohash-error-message')
    expect(html).toContain('var')
  })

  it('handles multiple sequential codeToHtml calls correctly', async () => {
    const secondResult: TwohashResult = {
      ...errorResult,
    }

    const resultMap: TwohashResultMap = new Map()
    resultMap.set(hashCode(sampleResult.original), sampleResult)
    resultMap.set(hashCode(errorResult.original), secondResult)

    const transformer = transformerTwohashFromMap(resultMap)

    // First call: hover result
    const html1 = await codeToHtml(sampleResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformer],
    })
    expect(html1).toContain('twohash-hover')
    expect(html1).not.toContain('twohash-error-message')

    // Second call: error result
    const html2 = await codeToHtml(errorResult.original, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformer],
    })
    expect(html2).toContain('twohash-error-message')
    expect(html2).toContain('CS0103')

    // Third call: code not in map — no-op
    const html3 = await codeToHtml('var z = 0;', {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformer],
    })
    expect(html3).not.toContain('twohash-hover')
    expect(html3).not.toContain('twohash-error-message')
  })

  it('has name property set to twohash', () => {
    const resultMap: TwohashResultMap = new Map()
    const transformer = transformerTwohashFromMap(resultMap)
    expect(transformer.name).toBe('twohash')
  })
})

describe('processTwohashBlocks', () => {
  // Mock the twohash bridge to avoid needing the real CLI
  vi.mock('twohash', () => ({
    createTwohash: vi.fn(() => {
      const cache = new Map<string, TwohashResult>()
      let callCount = 0
      return {
        process: vi.fn(async (opts: { code: string; project?: string; region?: string }): Promise<TwohashResult> => {
          const cacheKey = createHash('sha256').update(opts.code).digest('hex')
          if (cache.has(cacheKey)) return cache.get(cacheKey)!

          callCount++
          const result: TwohashResult = {
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

    const resultMap = await processTwohashBlocks(blocks)

    expect(resultMap.size).toBe(2)
    expect(resultMap.has(hashCode(blocks[0]))).toBe(true)
    expect(resultMap.has(hashCode(blocks[1]))).toBe(true)
  })

  it('skips blocks without markers', async () => {
    const blocks = [
      'var x = 42;',           // no markers
      'var y = 100;\n//   ^?', // has markers
    ]

    const resultMap = await processTwohashBlocks(blocks)

    expect(resultMap.size).toBe(1)
    expect(resultMap.has(hashCode(blocks[0]))).toBe(false)
    expect(resultMap.has(hashCode(blocks[1]))).toBe(true)
  })

  it('returns empty map for empty input', async () => {
    const resultMap = await processTwohashBlocks([])
    expect(resultMap.size).toBe(0)
  })

  it('returns empty map when no blocks have markers', async () => {
    const resultMap = await processTwohashBlocks(['var x = 42;', 'var y = 100;'])
    expect(resultMap.size).toBe(0)
  })

  it('passes per-block project/region overrides', async () => {
    const blocks = [
      { code: 'var x = 42;\n//  ^?', project: './A.csproj' },
      { code: 'var y = 100;\n//   ^?', region: 'intro' },
    ]

    const resultMap = await processTwohashBlocks(blocks, { project: './B.csproj' })

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

    const resultMap = await processTwohashBlocks(blocks)

    // Should only have one entry (all three are identical)
    expect(resultMap.size).toBe(1)

    // The result should be the same for all (call count embedded in text)
    const result = resultMap.get(hashCode(code))!
    expect(result.hovers[0].text).toContain('call 1')
  })
})
