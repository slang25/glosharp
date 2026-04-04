## Context

GloSharp extracts hover information from C# code using Roslyn. The `ExtractDocComment()` method in `GloSharpProcessor.cs` calls `symbol.GetDocumentationCommentXml()` which returns the full XML doc comment, but currently only the `<summary>` element is extracted. The result is stored as `GloSharpHover.Docs` — a plain `string?`.

The XML returned by Roslyn can contain rich structured documentation: `<param>`, `<returns>`, `<remarks>`, `<example>`, `<exception>`, `<typeparam>`, `<seealso>`, and more. VS Code tooltips display all of these — glosharp should too.

The data flows through: C# Models → JSON serialization → Node bridge types → EC plugin rendering.

## Goals / Non-Goals

**Goals:**
- Extract `<summary>`, `<param>`, `<returns>`, `<remarks>`, `<example>`, and `<exception>` tags from XML doc comments
- Represent docs as a structured object (`GloSharpDocComment`) instead of a flat string
- Render all doc sections in hover popups with clear visual hierarchy
- Maintain null semantics: `docs` is null when no documentation exists

**Non-Goals:**
- Inline XML references (`<see cref="..."/>`, `<paramref name="..."/>`) — render as plain text for now, not as links
- `<typeparam>` extraction — can be added later alongside generic type support improvements
- `<seealso>` extraction — low value for hover popups
- Markdown rendering within doc comment text — keep as plain text
- Backward-compatible string fallback — this is a clean breaking change

## Decisions

### 1. Structured object vs tagged array for docs

**Options:**
- (a) **Typed object** — `GloSharpDocComment` with named fields: `summary`, `params`, `returns`, `remarks`, `examples`, `exceptions`
- (b) **Tagged array** — `{ tag: "param", name: "value", text: "The value" }[]`

**Decision: (a) Typed object**

Rationale: The set of XML doc tags is well-known and stable. Named fields give better TypeScript autocompletion, clearer rendering code, and the JSON is self-documenting. A tagged array would be more flexible but adds unnecessary indirection for a fixed schema.

### 2. Null vs empty object when no docs exist

**Options:**
- (a) **Null** — `docs: null` when no XML docs
- (b) **Empty object** — `docs: { summary: null, params: [], ... }`

**Decision: (a) Null**

Rationale: Consistent with current behavior. The existing `JsonIgnoreCondition.WhenWritingNull` serialization config means `docs` is omitted entirely when null, keeping output compact for symbols without documentation. Consumers already handle the null case.

### 3. Params as named list vs positional array

**Decision:** `params` is an array of `{ name: string, text: string }` objects, preserving parameter order from the XML. This maps naturally to rendering a param table and allows matching params to the method signature.

### 4. Inline XML element handling (`<see>`, `<paramref>`, `<c>`)

**Decision:** Strip XML element tags and use their inner text. `<see cref="String"/>` becomes `"String"`, `<paramref name="x"/>` becomes `"x"`, `<c>code</c>` becomes `"code"`. This keeps the text useful without requiring a rich text model. Can be enhanced later with a tagged-text approach if needed.

### 5. Where to place new CSS for doc sections

**Decision:** Add new CSS rules to the existing `buildBaseStyles()` function in `plugin.ts`. Each doc section gets a class: `.glosharp-popup-params`, `.glosharp-popup-returns`, `.glosharp-popup-remarks`, `.glosharp-popup-example`, `.glosharp-popup-exceptions`. All share the separator border pattern already used by `.glosharp-popup-docs`.

## Risks / Trade-offs

- **[Breaking change]** → The `docs` field type changes from `string | null` to `GloSharpDocComment | null`. Any consumers reading `hover.docs` as a string will break. Mitigated by: this is pre-1.0 software with no external consumers yet.
- **[Verbose JSON for rich docs]** → Methods like `Console.WriteLine` have extensive docs with many params. Mitigated by: `JsonIgnoreCondition.WhenWritingNull` omits empty fields, and arrays default to empty `[]` which is compact.
- **[XML parsing edge cases]** → Malformed XML, CDATA sections, nested elements. Mitigated by: keep the existing try-catch pattern; malformed docs degrade to null rather than crashing.
