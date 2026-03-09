## Why

Twohash's design docs and data format already define presentation markers (`@highlight`, `@focus`, `@diff`) but they aren't implemented. Documentation authors need these to draw attention to specific lines, show before/after diffs, and focus readers on relevant code — capabilities that twoslash and Expressive Code both support, and that are essential for effective code walkthroughs.

## What Changes

- **MarkerParser extended**: Parses `// @highlight`, `// @focus`, and `// @diff` directive comments with line-range and next-line targeting
- **Highlight model**: `TwohashResult.Highlights` changes from `List<object>` (empty placeholder) to `List<TwohashHighlight>` with `line`, `character`, `length`, `kind` fields
- **JSON output**: `highlights` array populates with entries of kind `"highlight"`, `"focus"`, `"add"`, or `"remove"`
- **TypeScript types**: `highlights: unknown[]` becomes `highlights: TwohashHighlight[]` with a proper interface
- **EC plugin rendering**: New annotation classes for highlight (background color), focus (dim non-focused lines), and diff (green/red line backgrounds) with theme-aware CSS

## Capabilities

### New Capabilities
- `directive-markers`: Parsing and processing of `@highlight`, `@focus`, and `@diff` comment directives into the highlights output array

### Modified Capabilities
- `marker-parsing`: New requirements for recognizing `@highlight`, `@focus`, and `@diff` directive syntax and stripping marker lines from output
- `json-output`: `highlights` field changes from empty placeholder to populated `TwohashHighlight[]`
- `node-bridge`: TypeScript types updated with `TwohashHighlight` interface
- `expressive-code-plugin`: Rendering of highlight, focus, and diff annotations with appropriate CSS styling

## Impact

- **C# core** (`src/TwoHash.Core/`): `MarkerParser.cs` (new directive parsing), `Models.cs` (new `TwohashHighlight` record), `TwohashProcessor.cs` (wire highlights to output)
- **Node bridge** (`packages/twohash/src/types.ts`): New `TwohashHighlight` type, update `TwohashResult.highlights`
- **EC plugin** (`packages/expressive-code/src/plugin.ts`): New annotation classes + CSS for highlight/focus/diff
- **Tests** (`tests/TwoHash.Tests/`): Marker parsing tests, integration tests for highlight output
