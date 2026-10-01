/**
 * Canonical form of a fence body — the CI artifact builder and the webframe
 * shell both hash this, so it has exactly one definition, shared with every
 * other Glo# integration: `@glosharp/core`'s `canonicalizeSnippet` (from its
 * Node-free entry point, so the GitBook integration bundle stays Worker-safe).
 *
 * CRLF → LF and leading/trailing blank space dropped; nothing inside the
 * snippet is touched.
 */
export { canonicalizeSnippet } from '@glosharp/core/snippet'

/** Length of the hex artifact key, i.e. SHA-256. */
export const SNIPPET_KEY_LENGTH = 64

/** True for a string shaped like an artifact key. */
export function isSnippetKey(value: string): boolean {
  return value.length === SNIPPET_KEY_LENGTH && /^[0-9a-f]+$/.test(value)
}

/** File name an artifact is published under: `<sha256>.html`. */
export const ARTIFACT_FILE_PATTERN = /^[0-9a-f]{64}\.html$/

/** Theme names become directory names and URL path segments; keep them boring. */
export const THEME_NAME_PATTERN = /^[a-z0-9][a-z0-9-]*$/i

export function isThemeName(value: string): boolean {
  return THEME_NAME_PATTERN.test(value)
}
