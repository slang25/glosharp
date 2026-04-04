## 1. C# Models

- [x] 1.1 Add `GloSharpDocComment`, `GloSharpDocParam`, and `GloSharpDocException` classes to `Models.cs`
- [x] 1.2 Change `GloSharpHover.Docs` from `string?` to `GloSharpDocComment?`

## 2. Core Extraction

- [x] 2.1 Rewrite `ExtractDocComment()` in `GloSharpProcessor.cs` to return `GloSharpDocComment?` — extract `<summary>`, `<param>`, `<returns>`, `<remarks>`, `<example>`, `<exception>` tags
- [x] 2.2 Add inline XML element handling: resolve `<see cref>`, `<paramref name>`, `<c>` to plain text
- [x] 2.3 Strip `T:`, `M:`, `P:`, `F:` prefixes from `cref` attribute values in `<exception>` and `<see>` tags

## 3. TypeScript Types

- [x] 3.1 Add `GloSharpDocComment`, `GloSharpDocParam`, `GloSharpDocException` interfaces to `packages/glosharp/src/types.ts`
- [x] 3.2 Update `GloSharpHover.docs` type from `string | null` to `GloSharpDocComment | null`

## 4. EC Plugin Rendering

- [x] 4.1 Update `GloSharpHoverAnnotation.render()` in `packages/expressive-code/src/plugin.ts` to render structured docs sections (summary, params, returns, remarks, examples, exceptions)
- [x] 4.2 Add CSS classes and styles for doc sections: `.glosharp-popup-params`, `.glosharp-popup-returns`, `.glosharp-popup-remarks`, `.glosharp-popup-example`, `.glosharp-popup-exceptions`

## 5. Tests

- [x] 5.1 Add C# tests for `ExtractDocComment` with all tag types, inline XML elements, malformed XML, and null cases
- [x] 5.2 Update existing hover tests that assert on the `Docs` field to use the new structured type
