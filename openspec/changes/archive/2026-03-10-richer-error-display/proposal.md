## Why

GloSharp already extracts diagnostic severity from Roslyn (error, warning, info) but renders all diagnostics identically with red styling. Documentation authors showing warnings (e.g., obsolete API usage, nullable warnings) get confusing visuals indistinguishable from hard errors. Error codes like CS1002 are displayed as plain text when they could link directly to Microsoft's documentation. Multi-line diagnostic spans are truncated to a single line, losing context for diagnostics like unterminated literals or multi-line expression errors.

## What Changes

- **Severity-differentiated styling**: Warnings render with yellow/amber wavy underlines and message styling; info diagnostics use blue styling; errors remain red. Applied across all three renderers (EC plugin, Shiki transformer, standalone HTML).
- **Clickable error codes**: Error codes (e.g., CS1002) become hyperlinks to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/` in rendered output. Links open in new tabs.
- **Multi-line error spans**: Add `endLine` and `endCharacter` fields to the error model. When a Roslyn diagnostic spans multiple lines, underline styling extends across all affected lines instead of truncating to the first line.
- **Theme expansion**: Built-in themes (github-dark, github-light) gain warning and info color definitions.

## Capabilities

### New Capabilities

- `severity-styling`: Severity-aware visual differentiation for error, warning, and info diagnostics across all renderers.
- `error-code-links`: Clickable error code hyperlinks to Microsoft documentation in rendered output.
- `multi-line-error-spans`: Support for diagnostic spans that cross line boundaries in the data model and all renderers.

### Modified Capabilities

- `roslyn-extraction`: Diagnostic extraction must populate end-position fields for multi-line spans.
- `json-output`: Error objects gain optional `endLine` and `endCharacter` fields.
- `html-renderer`: Standalone renderer must support severity colors, clickable codes, and multi-line underlines.
- `expressive-code-plugin`: EC error annotations must differentiate by severity, render clickable codes, and handle multi-line spans.
- `shiki-transformer`: Shiki error injection must differentiate by severity, render clickable codes, and handle multi-line spans.
- `node-bridge`: TypeScript types updated with new optional fields.

## Impact

- **C# Core**: `GloSharpError` model, `GloSharpProcessor` diagnostic extraction, `GloSharpTheme`, `HtmlRenderer`
- **TypeScript packages**: `types.ts`, EC `plugin.ts`, Shiki `transformer.ts`
- **JSON contract**: Additive — new optional fields `endLine` and `endCharacter` on error objects. Non-breaking for existing consumers.
- **Tests**: New test cases for multi-line diagnostics, severity-based rendering, and error code URL generation.
