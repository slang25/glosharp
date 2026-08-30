/**
 * Canonical form of a fence body.
 *
 * The CI artifact builder and the webframe shell both hash this — if they
 * disagree by a single byte the lookup misses and the reader sees plain code,
 * so the rule has to be simple enough to reimplement anywhere.
 *
 * Deliberately minimal: line endings are normalised and leading/trailing blank
 * space is dropped (GitBook's editor round-trips those freely), but nothing
 * inside the snippet is touched — trailing whitespace on an interior line can
 * be meaningful inside a raw string literal.
 *
 * MUST stay self-contained (no imports, no helpers, ES2020 string ops only):
 * `frame.ts` serialises it into the shell script with
 * `Function.prototype.toString()` so there is exactly one definition.
 */
export function canonicalizeSnippet(code: string): string {
  return code
    .replace(/\r\n?/g, '\n')
    // Whole whitespace-only lines, so a leading "  \n" is dropped like a bare
    // "\n" is — indentation on the first line that has content survives.
    .replace(/^(?:[ \t]*\n)+/, '')
    .replace(/[ \t\n]+$/, '')
}

/** Length of the hex artifact key, i.e. SHA-256. */
export const SNIPPET_KEY_LENGTH = 64

/** True for a string shaped like an artifact key. */
export function isSnippetKey(value: string): boolean {
  return value.length === SNIPPET_KEY_LENGTH && /^[0-9a-f]+$/.test(value)
}
