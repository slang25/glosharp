## Context

Twohash already extracts Roslyn diagnostics with full severity metadata (`error`, `warning`, `info`, `hidden`) and surfaces them through the JSON contract. However, all three renderers (EC plugin, Shiki transformer, standalone HTML) treat every diagnostic identically — red wavy underlines regardless of severity. Error codes are plain text. Diagnostic spans are assumed to be single-line.

The existing `TwohashError` model has `line`, `character`, `length` but no end position. Roslyn's `Diagnostic.Location.SourceSpan` provides full start/end positions, but we currently only extract the start + length.

## Goals / Non-Goals

**Goals:**
- Visually differentiate warnings (yellow/amber) and info (blue) from errors (red) in all renderers
- Make error codes clickable links to Microsoft's compiler message documentation
- Support multi-line diagnostic spans in the data model and renderers
- Keep changes additive and non-breaking for existing JSON consumers

**Non-Goals:**
- Quick-fix suggestions from Roslyn's `CodeFixProvider` (separate future feature)
- Custom severity icons/glyphs (text/color differentiation is sufficient)
- Supporting non-C# diagnostic code URL schemes

## Decisions

### 1. Multi-line span representation: `endLine`/`endCharacter` fields

**Options:**
- (a) Add `endLine` and `endCharacter` optional fields to `TwohashError`
- (b) Replace `length` with a `range` object `{ start: { line, character }, end: { line, character } }`
- (c) Flatten multi-line spans into per-line segments at extraction time

**Decision: (a)** — Add optional `endLine`/`endCharacter`. This is additive (non-breaking), consistent with the existing flat field style, and avoids the complexity of breaking the current `line`/`character`/`length` contract. When both start and end are on the same line, these fields are omitted (length suffices). When the span crosses lines, `endLine` and `endCharacter` indicate where it ends. Renderers that don't understand multi-line spans can fall back to `length` on the first line.

### 2. Error code URL pattern

Microsoft C# compiler messages live at two URL patterns:
- `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs{NNNN}` (common errors)
- `https://learn.microsoft.com/en-us/dotnet/csharp/misc/cs{NNNN}` (less common)

**Decision:** Link to the first pattern (compiler-messages). This handles the most common cases and Microsoft redirects from the misc path for well-known errors. The URL is constructed client-side from the error code — no lookup table needed. Only codes matching the `CS\d+` pattern get links; analyzer codes (e.g., `CA1234`, `IDE0001`) are left as plain text since their docs live in different locations.

### 3. Severity color palette

Follow established IDE conventions:
- **Error**: Red — existing colors unchanged (`#f85149` dark, `#cf222e` light)
- **Warning**: Yellow/amber — `#d29922` dark, `#9a6700` light (GitHub's warning colors)
- **Info**: Blue — `#539bf5` dark, `#0969da` light (GitHub's info colors)

These map to the same hues used by VS Code, Visual Studio, and GitHub UI for consistency.

### 4. Multi-line underline rendering strategy

**Options:**
- (a) Underline each affected line from start to end of that line's content
- (b) Only underline the first line (current behavior, ignore multi-line)
- (c) Underline first line start→end, middle lines full, last line start→end character

**Decision: (c)** — Full multi-line rendering. First line: from `character` to end of line. Middle lines: full line. Last line: from start to `endCharacter`. This matches VS Code behavior. The error message div appears after the last affected line.

### 5. Where to construct URLs

**Decision:** URLs are constructed in the renderers (HTML, EC, Shiki), not in the C# core. The core outputs the raw `code` string. Renderers wrap it in an `<a>` tag when the code matches `^CS\d+$`. This keeps the JSON contract clean and renderer-agnostic.

## Risks / Trade-offs

- **[Microsoft URL changes]** → The docs URL pattern could change. Mitigation: URL construction is in renderer code, easy to update. No server-side dependency.
- **[Multi-line spans are rare]** → Most C# diagnostics are single-line. The feature adds complexity for edge cases. Mitigation: Multi-line fields are optional; single-line rendering path unchanged.
- **[Color accessibility]** → Yellow warning text on light backgrounds can be low-contrast. Mitigation: Using GitHub's established amber palette which passes WCAG AA on white backgrounds.
