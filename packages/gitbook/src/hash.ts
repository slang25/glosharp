import { createHash } from 'node:crypto'
import { canonicalizeSnippet } from './snippet-key.js'

/**
 * Artifact key for a fence body: SHA-256 (hex) of its canonical form.
 * The webframe shell computes the same value with `crypto.subtle`.
 */
export function snippetKey(code: string): string {
  return createHash('sha256').update(canonicalizeSnippet(code), 'utf8').digest('hex')
}
