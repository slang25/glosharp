## Why

The official expressive-code-twoslash plugin supports custom tag directives (`@log`, `@warn`, `@error`, `@annotate`) that render as styled callout boxes alongside code. These are widely used in TypeScript documentation for inline annotations — informational notes, warnings, error explanations, and general commentary. GloSharp currently has no equivalent, leaving documentation authors without a way to add contextual callouts to C# code blocks.

## What Changes

- **MarkerParser extended**: Parses `// @log: message`, `// @warn: message`, `// @error: message`, and `// @annotate: message` directive comments. Each tag is associated with the preceding code line and stripped from output.
- **Tag model**: New `GloSharpTag` record with `name`, `text`, and `line` fields
- **JSON output**: New `tags` array in `GloSharpResult` containing tag entries
- **TypeScript types**: New `GloSharpTag` interface and `tags: GloSharpTag[]` on `GloSharpResult`
- **EC plugin rendering**: New `GloSharpCustomTagAnnotation` class rendering callout boxes with tag-specific icons and theme-aware colors (log=blue, warn=amber, error=red, annotate=purple)

## Capabilities

### New Capabilities
- `custom-tags`: Parsing and rendering of `@log`, `@warn`, `@error`, and `@annotate` directive comments as styled callout annotations

### Modified Capabilities
- `marker-parsing`: New directive syntax for `@log`, `@warn`, `@error`, `@annotate` with message text
- `json-output`: New `tags` array in the top-level JSON output
- `node-bridge`: New `GloSharpTag` TypeScript interface and updated `GloSharpResult`
- `expressive-code-plugin`: New annotation class for custom tag callout rendering with icons and theme-aware styling

## Impact

- **C# core** (`src/GloSharp.Core/`): `MarkerParser.cs` (new tag directive parsing), `Models.cs` (new `GloSharpTag` record), `GloSharpProcessor.cs` (wire tags to output)
- **Node bridge** (`packages/glosharp/src/types.ts`): New `GloSharpTag` type, update `GloSharpResult.tags`
- **EC plugin** (`packages/expressive-code/src/plugin.ts`): New annotation class + CSS + SVG icons for tag callouts
- **Tests** (`tests/TwoHash.Tests/`): Marker parsing tests, integration tests for tag output
