## 1. Data Model

- [x] 1.1 Add `GloSharpTypeAnnotation` record to `Models.cs` with `Name` and `Expansion` string properties
- [x] 1.2 Add optional `TypeAnnotations` property (`List<GloSharpTypeAnnotation>?`) to `GloSharpHover`

## 2. Anonymous Type Detection & Placeholder Assignment

- [x] 2.1 Create `AnonymousTypeFormatter` class in `GloSharp.Core` with method to detect anonymous types in a symbol's type hierarchy using `INamedTypeSymbol.IsAnonymousType`
- [x] 2.2 Implement placeholder assignment logic — sequential `'a`, `'b`, `'c` per-hover scope, with same anonymous type always mapping to the same letter
- [x] 2.3 Implement expansion text builder — walk `INamedTypeSymbol.GetMembers().OfType<IPropertySymbol>()` to format as `new { Type1 Prop1, Type2 Prop2 }`, using the existing `SymbolDisplayFormat` for property types
- [x] 2.4 Handle nested anonymous types recursively — when a property type is itself anonymous, assign it a separate placeholder and add its own annotation entry

## 3. Display Part Transformation

- [x] 3.1 In `BuildHoverFromToken`, after `ToDisplayParts()`, post-process the parts list to replace anonymous type name tokens with their assigned placeholder text
- [x] 3.2 Populate `TypeAnnotations` on the resulting `GloSharpHover` with the collected placeholder-to-expansion mappings
- [x] 3.3 Handle array/nullable wrappers — for types like `'a[]` or `'a?`, replace only the anonymous type name portion while preserving the array/nullable syntax in display parts

## 4. Tests

- [x] 4.1 Test hover on `var` declaration of a simple anonymous type — verify placeholder in text and typeAnnotations populated
- [x] 4.2 Test hover on `var` declaration of an array of anonymous types — verify `'a[]` display
- [x] 4.3 Test hover on anonymous type property access — verify `string 'a.Name { get; }` format with annotation
- [x] 4.4 Test hover with nested anonymous types — verify multiple annotations (`'a`, `'b`) with correct expansions
- [x] 4.5 Test hover on non-anonymous type — verify typeAnnotations is null/omitted
- [x] 4.6 Test LINQ projection producing anonymous type — verify placeholder in inferred result type

## 5. JSON Output

- [x] 5.1 Verify `typeAnnotations` serializes correctly in JSON output — present when populated, omitted when null
- [x] 5.2 Update snapshot/golden tests if applicable to include anonymous type hover examples
