## Context

GloSharp already supports several directive markers (`@highlight`, `@focus`, `@diff`, `@errors`, `@noErrors`) that are parsed from comments, stripped from output, and rendered in the EC plugin. The official expressive-code-twoslash plugin additionally supports custom tag directives (`@log`, `@warn`, `@error`, `@annotate`) that render as styled callout boxes alongside code lines. These tags use the same `// @name: value` comment pattern as existing directives, making them a natural extension of the current marker parsing infrastructure.

In the twoslash ecosystem, custom tags are provided via a `customTags` configuration array and produce `NodeTag` entries with `name` and `text` fields. The EC plugin renders these as `TwoslashCustomTagsAnnotation` — a styled box with an icon, title, and message.

## Goals / Non-Goals

**Goals:**
- Parse `@log`, `@warn`, `@error`, and `@annotate` directives with message text
- Produce `GloSharpTag` entries in a new `tags` array in the JSON output
- Render tag callout boxes in the EC plugin with tag-specific icons and theme-aware colors
- Strip tag directive lines from output code (same behavior as existing markers)

**Non-Goals:**
- User-configurable custom tag names (only the four built-in tags for now)
- Markdown or rich text rendering inside tag messages (plain text only)
- Shiki transformer rendering (EC plugin only)
- Multi-line tag messages spanning multiple comment lines

## Decisions

### 1. Tags target the preceding code line

A tag directive associates with the code line immediately before it in the processed output:

```csharp
var x = 42;
// @log: x is assigned the value 42
```

The tag's `line` field points to the line containing `var x = 42;` (after marker removal). This matches how twoslash custom tags work — they annotate the line above, not below.

**Rationale**: This is consistent with how `^?` hover markers work (targeting the preceding line) and matches the twoslash convention. It reads naturally: the code comes first, then the annotation about it.

### 2. Tags stored as a separate `tags` array (not in `highlights`)

Tags are semantically different from highlights — they carry text content and render as callout boxes rather than line-level decorations. A dedicated `tags` array keeps the data model clean.

**Rationale**: Mixing tags into `highlights` would require adding optional `name` and `text` fields and a new `kind` value, complicating the existing highlight model. A separate array is cleaner and matches how twoslash separates `tags` from other node types.

### 3. Four fixed tag names with distinct visual treatment

| Tag | Icon | Color |
|-----|------|-------|
| `log` | Info circle (ℹ) | Blue |
| `warn` | Warning triangle (⚠) | Amber |
| `error` | Error circle (⊘) | Red |
| `annotate` | Lightbulb (💡) | Purple |

**Rationale**: These match the expressive-code-twoslash defaults exactly. Keeping the set fixed avoids configuration complexity. The four tags cover the most common annotation needs.

### 4. Tag message is everything after the colon

The syntax is `// @tag: message text here`. The message is the trimmed text after `@tag:`. A tag without a message (e.g., bare `// @log`) is not valid and should be ignored (not treated as a tag).

**Rationale**: Tags without messages provide no value as callouts. Requiring the colon and message ensures every tag renders meaningfully. This matches the twoslash behavior where `@tag` without a value is treated as a compiler flag, not a custom tag.

### 5. EC rendering as block annotation below the code line

Tags render as a full-width callout box inserted below the code line they annotate, similar to how error messages are displayed. The box contains an icon, the tag name as a title, and the message text.

**Rationale**: This matches the expressive-code-twoslash rendering pattern. Block-level rendering (vs inline) gives callouts enough space to display their content clearly.

### 6. CSS class naming: `glosharp-tag-{name}`

Tag boxes use `glosharp-tag` as the base class and `glosharp-tag-log`, `glosharp-tag-warn`, `glosharp-tag-error`, `glosharp-tag-annotate` for tag-specific styling.

**Rationale**: Consistent with existing naming (`glosharp-hover`, `glosharp-highlight`, etc.) and allows targeted CSS overrides per tag type.

## Risks / Trade-offs

- **`@error` tag vs `@errors` directive**: The `@error` tag (callout) and `@errors` directive (expected compiler errors) have very similar names. Mitigation: `@errors` requires a colon followed by error codes (`CS1002`), while `@error` requires a colon followed by free-text message. The parser can distinguish by checking if the text after the colon matches the error code pattern. `@errors` (plural) is the existing directive; `@error` (singular) is the custom tag.
- **Tags on first line**: If a tag appears on the very first line with no preceding code line, the `line` would be -1. Mitigation: tags on the first line (before any code) target line 0 (the first output line).
- **Plain text only**: Tag messages don't support markdown or links. Mitigation: plain text covers the common case; rich content can be added later if needed.
