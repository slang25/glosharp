import { describe, it, expect } from 'vitest'
import { createHash } from 'node:crypto'
import { snippetKey } from '../src/key.js'
import { canonicalizeSnippet, hasGloSharpMarkers } from '../src/snippet.js'
import { unexpectedErrors } from '../src/errors.js'
import type { GloSharpError, GloSharpResult } from '../src/types.js'

describe('snippetKey', () => {
  it('ignores line-ending and trailing/leading blank-line differences', () => {
    const base = snippetKey('var x = 1;\nvar y = 2;')
    expect(snippetKey('var x = 1;\r\nvar y = 2;\r\n')).toBe(base)
    expect(snippetKey('\n\nvar x = 1;\nvar y = 2;\n\n')).toBe(base)
  })

  it('keeps interior whitespace', () => {
    expect(snippetKey('var s = """\n  a  \n""";')).not.toBe(snippetKey('var s = """\n  a\n""";'))
  })

  it('is sha256 of the canonical snippet when no options are set', () => {
    const code = 'var x = 1;\n'
    const expected = createHash('sha256').update(canonicalizeSnippet(code)).digest('hex')
    expect(snippetKey(code)).toBe(expected)
    expect(snippetKey(code, {})).toBe(expected)
    expect(snippetKey(code, { project: undefined, noRestore: false })).toBe(expected)
  })

  it('includes every result-affecting option', () => {
    const code = 'var x = 1;'
    const keys = new Set([
      snippetKey(code),
      snippetKey(code, { project: 'a.csproj' }),
      snippetKey(code, { project: 'b.csproj' }),
      snippetKey(code, { framework: 'net10.0' }),
      snippetKey(code, { region: 'r' }),
      snippetKey(code, { complog: 'x.glocontext' }),
      snippetKey(code, { configFile: 'glosharp.config.json' }),
      snippetKey(code, { noRestore: true }),
    ])
    expect(keys.size).toBe(8)
  })

  it('does not depend on option key order', () => {
    expect(snippetKey('x', { project: 'p', framework: 'f' })).toBe(snippetKey('x', { framework: 'f', project: 'p' }))
  })
})

describe('hasGloSharpMarkers', () => {
  it.each([
    'var x = 1;\n//  ^?',
    'Console.\n//      ^|',
    '// @errors: CS0029\nint x = "";',
    '// @noErrors\nfoo();',
    '// @suppressErrors\nfoo();',
    'class A {}\n// ---cut---\nvar a = new A();',
    '// ---cut-after---',
    '// @highlight\nvar x = 1;',
    '// @focus\nvar x = 1;',
    '// @diff: +\nvar x = 1;',
    '// @log: hi\nvar x = 1;',
    '// @langVersion: 12',
    '#:package Humanizer@2.14.1\nusing Humanizer;',
  ])('detects %j', (code) => {
    expect(hasGloSharpMarkers(code)).toBe(true)
  })

  it.each([
    'var x = 1;',
    'public void Foo() { /* ... */ }',
    '// a comment mentioning @errors without the colon form\nvar x = 1;',
    'var s = "// ^?";',
  ])('ignores %j', (code) => {
    expect(hasGloSharpMarkers(code)).toBe(false)
  })
})

describe('unexpectedErrors', () => {
  const error = (over: Partial<GloSharpError>): GloSharpError => ({
    line: 0, character: 0, length: 1, code: 'CS0103', message: 'm', severity: 'error', expected: false, ...over,
  })
  const result = (errors: GloSharpError[], hiddenErrors: GloSharpError[], ok: boolean) =>
    ({ errors, hiddenErrors, meta: { compileSucceeded: ok } }) as unknown as GloSharpResult

  it('is empty when the compile succeeded', () => {
    expect(unexpectedErrors(result([error({})], [], true))).toEqual([])
  })

  it('returns unexpected errors including hidden ones, not expected errors or warnings', () => {
    const visible = error({ code: 'CS0103' })
    const hidden = error({ code: 'CS0246' })
    const out = unexpectedErrors(
      result([visible, error({ expected: true }), error({ severity: 'warning' })], [hidden], false),
    )
    expect(out).toEqual([visible, hidden])
  })
})
