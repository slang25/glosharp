## Why

Real-world usage of glosharp in an Astro blog revealed three issues that hurt the experience for both content authors and readers: keywords like `case` and `break` show incorrect hover info (the containing method instead of nothing), suppressing compilation errors for unavailable frameworks requires tedious per-line annotations, and the EC plugin's `styleSettings` requires a consumer-side workaround.

## What Changes

- **Fix keyword hover fallback**: Stop emitting hovers for C# keywords (`case`, `break`, `switch`, `return`, `if`, `else`, etc.) that incorrectly resolve to a parent symbol via the syntax tree walk. Predefined type keywords (`int`, `string`, `void`) continue to work correctly.
- **Add block-level error suppression**: Introduce a `// @suppressErrors` directive that suppresses all compilation errors for the block (best-effort mode), and `// @suppressErrors: CS0246, CS0103` to suppress specific codes block-wide. This eliminates the need for dozens of per-line `// @errors:` annotations when frameworks are unavailable.
- **Fix EC plugin styleSettings export**: Stop returning a plain `styleSettings` object from `pluginGloSharp()` since the styles are already embedded in `baseStyles`, removing the need for consumers to strip it with a workaround.

## Capabilities

### New Capabilities
- `block-error-suppression`: Block-level `@suppressErrors` directive for best-effort compilation scenarios

### Modified Capabilities
- `auto-hover-extraction`: Keywords must not produce hovers via parent-walk fallback
- `marker-parsing`: Add parsing for the new `@suppressErrors` directive
- `expressive-code-plugin`: Fix `styleSettings` export to avoid consumer workaround

## Impact

- **Core C#** (`src/GloSharp.Core/GloSharpProcessor.cs`): Modified hover extraction logic in `BuildHoverFromToken`
- **Core C#** (`src/GloSharp.Core/MarkerParser.cs`): New directive parsing, new fields on `MarkerParseResult`
- **Core C#** (`src/GloSharp.Core/GloSharpProcessor.cs`): Modified diagnostic extraction to honor block-level suppression
- **EC plugin** (`packages/expressive-code/src/plugin.ts`): Remove or fix `styleSettings` export
- **Node types** (`packages/glosharp/src/types.ts`): May need updates if suppression state is surfaced in output
- **Tests**: New test cases for keyword filtering, directive parsing, and end-to-end suppression
