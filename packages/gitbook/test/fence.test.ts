import { describe, it, expect } from 'vitest'
import { findFences, parseFenceAttributes } from '../src/fence.js'

describe('findFences', () => {
  it('finds a fence by language', () => {
    const markdown = ['# Title', '', '```glosharp', 'var x = 42;', '```', ''].join('\n')

    const blocks = findFences(markdown, 'glosharp')

    expect(blocks).toHaveLength(1)
    expect(blocks[0].code).toBe('var x = 42;')
    expect(blocks[0].line).toBe(3)
  })

  it('ignores other languages', () => {
    const markdown = '```csharp\nvar x = 42;\n```\n```glosharp\nvar y = 1;\n```'

    expect(findFences(markdown, 'glosharp').map((b) => b.code)).toEqual(['var y = 1;'])
  })

  it('returns every fence when no language is given', () => {
    const markdown = '```csharp\na\n```\n\n```glosharp\nb\n```'

    expect(findFences(markdown).map((b) => b.lang)).toEqual(['csharp', 'glosharp'])
  })

  it('matches the language case-insensitively', () => {
    expect(findFences('```GloSharp\nvar x = 1;\n```', 'glosharp')).toHaveLength(1)
  })

  it('preserves interior blank lines and indentation', () => {
    const markdown = '```glosharp\nif (x)\n{\n\n    Run();\n}\n```'

    expect(findFences(markdown, 'glosharp')[0].code).toBe('if (x)\n{\n\n    Run();\n}')
  })

  it('strips the opening fence indentation from the body', () => {
    const markdown = ['- item', '', '  ```glosharp', '  var x = 42;', '  ```'].join('\n')

    expect(findFences(markdown, 'glosharp')[0].code).toBe('var x = 42;')
  })

  it('does not treat a longer inner fence as a terminator', () => {
    const markdown = '````glosharp\n```\nnested\n```\n````'

    expect(findFences(markdown, 'glosharp')[0].code).toBe('```\nnested\n```')
  })

  it('supports tilde fences and ignores backtick fences inside them', () => {
    const markdown = '~~~glosharp\n```\n~~~'

    expect(findFences(markdown, 'glosharp')[0].code).toBe('```')
  })

  it('runs an unterminated fence to end of file', () => {
    const markdown = '```glosharp\nvar x = 42;'

    expect(findFences(markdown, 'glosharp')[0].code).toBe('var x = 42;')
  })

  it('skips a fence whose info string contains a backtick', () => {
    expect(findFences('```glosharp `x`\nvar x = 1;\n```', 'glosharp')).toHaveLength(0)
  })

  it('does not find fences inside another fence body', () => {
    const markdown = '````md\n```glosharp\nvar x = 1;\n```\n````'

    expect(findFences(markdown, 'glosharp')).toHaveLength(0)
  })

  it('reports 1-based line numbers for every fence', () => {
    const markdown = ['a', '```glosharp', 'x', '```', 'b', '```glosharp', 'y', '```'].join('\n')

    expect(findFences(markdown, 'glosharp').map((b) => b.line)).toEqual([2, 6])
  })

  it('captures fence attributes', () => {
    const markdown = '```glosharp framework="net10.0" theme=github-light\nvar x = 1;\n```'

    expect(findFences(markdown, 'glosharp')[0].attributes).toEqual({
      framework: 'net10.0',
      theme: 'github-light',
    })
  })
})

describe('parseFenceAttributes', () => {
  it('parses quoted, unquoted, and bare attributes', () => {
    expect(parseFenceAttributes('a="one two" b=\'three\' c=four d')).toEqual({
      a: 'one two',
      b: 'three',
      c: 'four',
      d: true,
    })
  })

  it('returns an empty object for an empty tail', () => {
    expect(parseFenceAttributes('')).toEqual({})
    expect(parseFenceAttributes('   ')).toEqual({})
  })

  it('keeps the last value when a key repeats', () => {
    expect(parseFenceAttributes('theme=dark theme=light')).toEqual({ theme: 'light' })
  })
})
