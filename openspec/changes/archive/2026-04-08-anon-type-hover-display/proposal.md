## Why

Anonymous types in C# produce compiler-generated names (e.g., `<>f__AnonymousType0`) that Roslyn renders as raw display parts. The current hover output exposes these opaque names, making tooltips confusing and unhelpful. VS Code with C# Dev Kit solves this with a readable format — using placeholder names like `'a` and a supplementary "Types:" section that expands the anonymous type definition. We should adopt a similar approach so that hovers on anonymous-typed symbols are clear and informative.

## What Changes

- Detect anonymous types in hover display parts and replace compiler-generated names with short placeholder labels (`'a`, `'b`, etc.)
- Add structured `typeAnnotations` field to hover output mapping each placeholder to its expanded anonymous type shape (e.g., `{ name: "'a", expansion: "new { string Name, double[] Readings }" }`)
- Apply this formatting consistently across `var` declarations, `new` keyword hovers, and property access on anonymous types

## Capabilities

### New Capabilities
- `anon-type-display`: Formatting of anonymous types in hover output — replacing compiler-generated names with readable placeholders and adding a "Types:" expansion section

### Modified Capabilities
- `roslyn-extraction`: The hover building pipeline needs to detect anonymous type symbols and transform their display parts before emitting hover data

## Impact

- `src/GloSharp.Core/GloSharpProcessor.cs` — hover building and symbol display logic
- `src/GloSharp.Core/Models.cs` — may need additional fields on `GloSharpHover` for type annotations
- Tests in `tests/GloSharp.Tests/` — new test cases for anonymous type hover formatting
- Downstream renderers (HTML, Shiki, Expressive Code) may need updates if hover data structure changes
