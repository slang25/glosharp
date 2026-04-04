## Why

GloSharp currently only extracts `<summary>` from XML documentation comments, discarding valuable information like parameter descriptions, return values, remarks, and examples. Real-world .NET APIs have rich XML docs — showing only the summary makes hover popups less useful than VS Code tooltips, which is the quality bar we're targeting.

## What Changes

- **Structured doc extraction**: `ExtractDocComment()` will parse all standard XML doc tags (`<summary>`, `<param>`, `<returns>`, `<remarks>`, `<example>`, `<exception>`) into a structured object instead of returning a plain string
- **BREAKING**: `GloSharpHover.Docs` changes from `string?` to a `GloSharpDocComment?` object with typed fields for each tag category
- **JSON output**: The `docs` field in hover objects becomes a structured object (or null when no docs exist)
- **TypeScript types**: Updated to match the new structured docs shape
- **EC popup rendering**: Hover popups render param lists, return descriptions, remarks, examples, and exceptions below the summary in distinct styled sections

## Capabilities

### New Capabilities
- `structured-doc-extraction`: Parsing XML doc comments into structured objects with summary, params, returns, remarks, examples, and exceptions

### Modified Capabilities
- `roslyn-extraction`: Hover extraction produces structured doc comments instead of plain summary strings
- `json-output`: Hover `docs` field changes from `string | null` to a structured `GloSharpDocComment | null` object
- `node-bridge`: TypeScript types updated for the new `GloSharpDocComment` interface
- `expressive-code-plugin`: Popup rendering displays structured doc sections (params, returns, remarks, etc.)

## Impact

- **C# core** (`GloSharp.Core`): `Models.cs` (new `GloSharpDocComment` model), `GloSharpProcessor.cs` (`ExtractDocComment` rewrite)
- **JSON output**: `JsonOutput.cs` serialization handles new nested object (existing camelCase + omit-null config should work)
- **Node bridge** (`packages/glosharp`): `types.ts` type changes
- **EC plugin** (`packages/expressive-code`): `plugin.ts` popup rendering + new CSS for doc sections
- **Breaking change**: Consumers reading `docs` as a string will need to update to read `docs.summary`
