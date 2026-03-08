import { describe, it, expect } from 'vitest'
import { codeToHtml } from 'shiki'
import { transformerTwohashWithResult, type TransformerTwohashOptions } from '../src/transformer.js'
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
