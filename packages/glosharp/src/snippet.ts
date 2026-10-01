// Snippet helpers with no Node.js dependencies, so they can be bundled into
// browsers and edge runtimes (`@glosharp/core/snippet`).

/**
 * Canonical form of a snippet, used to key results across tools that hand the
 * same code block around slightly differently (Astro strips one trailing
 * newline before Shiki sees the code, markdown-it keeps it, Windows checkouts
 * add `\r`, GitBook's editor round-trips leading/trailing blank lines freely).
 *
 * Deliberately minimal: line endings are normalised and leading/trailing blank
 * space is dropped, but nothing inside the snippet is touched — trailing
 * whitespace on an interior line can be meaningful inside a raw string literal.
 *
 * MUST stay self-contained (no imports, no helpers, ES2020 string ops only):
 * `@glosharp/gitbook` serialises it into its webframe script with
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

/**
 * Matches any line carrying a Glo# marker or directive: `^?` / `^|` queries,
 * `@errors`, `@noErrors`, `@suppressErrors`, cut markers, `@highlight`,
 * `@focus`, `@diff`, `@langVersion`, `@nullable`, custom tags, and the
 * file-based-app `#:package` / `#:sdk` / `#:property` / `#:project` directives.
 * Mirrors `MarkerParser` / `FileDirectiveParser` in GloSharp.Core.
 */
export const GLOSHARP_MARKER_PATTERN =
  /^[ \t]*(?:\/\/[ \t]*(?:\^[?|]|@(?:errors:|noErrors\b|suppressErrors\b|highlight\b|focus\b|diff:|langVersion:|nullable:|log:|warn:|error:|annotate:)|---cut(?:-before|-after|-start|-end)?---)|#:(?:package|sdk|property|project)\b)/m

/** True when `code` contains at least one Glo# marker or directive. */
export function hasGloSharpMarkers(code: string): boolean {
  return GLOSHARP_MARKER_PATTERN.test(code)
}
