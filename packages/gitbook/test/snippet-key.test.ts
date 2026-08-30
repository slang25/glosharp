import { describe, it, expect } from 'vitest'
import { createHash, webcrypto } from 'node:crypto'
import { canonicalizeSnippet, isSnippetKey } from '../src/snippet-key.js'
import { snippetKey } from '../src/hash.js'
import { CANONICALIZE_SNIPPET_SOURCE, renderFrameShell } from '../src/frame.js'

describe('canonicalizeSnippet', () => {
  it('normalises CRLF and CR line endings', () => {
    expect(canonicalizeSnippet('a\r\nb\rc')).toBe('a\nb\nc')
  })

  it('drops leading and trailing blank lines', () => {
    expect(canonicalizeSnippet('\n\nvar x = 1;\n\n  \n')).toBe('var x = 1;')
  })

  it('preserves leading indentation on the first kept line', () => {
    expect(canonicalizeSnippet('\n    var x = 1;')).toBe('    var x = 1;')
  })

  it('preserves interior blank lines and interior trailing whitespace', () => {
    expect(canonicalizeSnippet('a   \n\nb')).toBe('a   \n\nb')
  })

  it('is idempotent', () => {
    const once = canonicalizeSnippet('\r\n  var x = 1;\r\n\r\n')
    expect(canonicalizeSnippet(once)).toBe(once)
  })
})

describe('snippetKey', () => {
  it('is the SHA-256 of the canonical form', () => {
    const code = 'var x = 42;'
    expect(snippetKey(code)).toBe(createHash('sha256').update(code, 'utf8').digest('hex'))
  })

  it('is stable across line-ending and surrounding-blank-line differences', () => {
    expect(snippetKey('var x = 42;')).toBe(snippetKey('\r\nvar x = 42;\r\n\r\n'))
  })

  it('changes when the code changes', () => {
    expect(snippetKey('var x = 42;')).not.toBe(snippetKey('var x = 43;'))
  })

  it('produces a value shaped like an artifact key', () => {
    expect(isSnippetKey(snippetKey('var x = 42;'))).toBe(true)
    expect(isSnippetKey('not-a-key')).toBe(false)
  })
})

/**
 * The shell script carries a serialised copy of `canonicalizeSnippet`. If the
 * two ever diverge every lookup misses, so pull the copy back out of the
 * generated script and check it against the original.
 */
describe('the copy of canonicalizeSnippet inlined into the frame shell', () => {
  const inlined = new Function(`return (${CANONICALIZE_SNIPPET_SOURCE})`)() as (c: string) => string

  it('is what the shell actually ships', () => {
    expect(renderFrameShell()).toContain(CANONICALIZE_SNIPPET_SOURCE)
  })

  it('behaves identically to the original', () => {
    const cases = ['a\r\nb', '\n\n  x  \n\n', 'a   \n\nb', '', '\n', 'if (x)\n{\n\n  y();\n}']

    for (const input of cases) {
      expect(inlined(input)).toBe(canonicalizeSnippet(input))
    }
  })

  it('hashes to the same key the builder writes', async () => {
    const code = '\r\nvar x = 42;\r\n\r\n'
    const digest = await webcrypto.subtle.digest('SHA-256', new TextEncoder().encode(inlined(code)))
    const hex = [...new Uint8Array(digest)].map((b) => b.toString(16).padStart(2, '0')).join('')

    expect(hex).toBe(snippetKey(code))
  })
})
