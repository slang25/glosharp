import { snippetKey as coreSnippetKey } from '@glosharp/core'

/**
 * Artifact key for a fence body: SHA-256 (hex) of its canonical form — the same
 * key every Glo# integration uses (`snippetKey` from `@glosharp/core`, with no
 * options). The webframe shell computes the same value with `crypto.subtle`.
 */
export function snippetKey(code: string): string {
  return coreSnippetKey(code)
}
