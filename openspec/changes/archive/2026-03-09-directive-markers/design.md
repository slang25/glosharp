## Context

GloSharp already defines a `highlights` placeholder in both the C# models (`List<object>`) and TypeScript types (`unknown[]`), and the data format spec describes the target shape with `line`, `character`, `length`, `kind` fields. The MarkerParser currently handles `^?`, `^|`, `@errors`, `@noErrors`, `@hide`/`@show`, and `---cut---`. The EC plugin renders hovers, errors, and completions. This change fills in the gap: parsing three new directive markers and rendering them.

## Goals / Non-Goals

**Goals:**
- Parse `@highlight`, `@focus`, and `@diff` directives with line-range and next-line targeting
- Produce `GloSharpHighlight` entries in the JSON output `highlights` array
- Render highlight, focus, and diff presentation in the EC plugin with theme-aware CSS
- Strip directive marker lines from output code (same behavior as existing markers)

**Non-Goals:**
- Character-level highlighting (only whole-line highlighting for this iteration)
- Shiki transformer rendering (EC plugin only; Shiki can consume the JSON later)
- Interactive diff viewer or side-by-side diff layout
- Combining directives on a single line (e.g., `// @highlight @focus`)

## Decisions

### 1. Line-level targeting only (no character ranges)

All three directives operate on whole lines. The data format supports `character` and `length` for future character-level highlighting, but these will be set to `0` and `line-length` respectively for now.

**Rationale**: Line-level presentation is the common case in documentation (matching Shiki/EC conventions). Character-level ranges add parser complexity without clear demand.

### 2. "Next line" targeting as the primary mode

Directives placed on a comment line target the next code line by default:

```csharp
// @highlight
var x = 42;          // this line gets highlighted

// @diff: +
var y = 99;          // this line is marked as added
```

For multi-line ranges, use colon syntax with line numbers (1-based, relative to output):

```csharp
// @highlight: 3-5   // highlight output lines 3, 4, 5
// @focus: 2-4       // focus output lines 2, 3, 4
```

**Rationale**: "Next line" is the most intuitive for inline use. Line-number ranges cover the multi-line case without needing to repeat the directive. This matches how `@errors` targets the next line.

### 3. Focus semantics: dim non-focused lines via CSS opacity

When any `focus` highlight exists, all lines without a focus marker get dimmed (opacity ~0.5). This is purely a CSS concern — the JSON output just records which lines are focused.

**Rationale**: Keeps the data format simple (just `kind: "focus"` entries). The rendering layer decides how to dim. EC and Shiki can implement differently.

### 4. Diff markers: `+` and `-` map to `add` and `remove` kinds

```csharp
// @diff: +    →  kind: "add"
// @diff: -    →  kind: "remove"
```

**Rationale**: Matches the existing data format spec which already defines `"add"` and `"remove"` as highlight kinds. The `+/-` syntax is familiar from diff notation.

### 5. Highlights stored as line-based entries (one entry per affected line)

A range directive like `// @highlight: 3-5` produces three separate `GloSharpHighlight` entries (one per line). This avoids range-based logic in renderers.

**Rationale**: EC annotations are per-line. Storing per-line entries simplifies the annotation mapping. Renderers iterate highlights and apply to matching lines directly.

### 6. MarkerParseResult gains a `Highlights` list

Add `List<HighlightDirective> Highlights` to `MarkerParseResult` alongside existing `HoverQueries`, `CompletionQueries`, etc. The `HighlightDirective` record captures kind and target line. These are remapped to processed-line coordinates just like hover queries.

### 7. EC plugin uses whole-line annotations (not inline range)

Highlight, focus, and diff annotations are applied to entire lines using EC's line-level annotation support, not `inlineRange`. This matches their whole-line visual treatment.

## Risks / Trade-offs

- **Line-number ranges reference output line numbers**: Authors must count lines in the output (after marker removal), which can be confusing. Mitigation: "next line" mode is the common case; ranges are an advanced feature.
- **Focus dimming is CSS-only**: If a renderer doesn't support the CSS, focused lines look identical to normal lines. Mitigation: focus is additive — the content is still correct without styling.
- **Per-line storage for ranges**: A `// @highlight: 1-100` produces 100 entries. Mitigation: documentation snippets are typically short (<50 lines); this won't be a practical issue.
