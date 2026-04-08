## MODIFIED Requirements

### Requirement: Extract structured display parts
The system SHALL use `ISymbol.ToDisplayParts()` to produce an array of display parts, each with a `kind` (mapped from `SymbolDisplayPartKind`) and `text` value. When the display parts contain anonymous type references, the system SHALL post-process the parts to replace compiler-generated type names with placeholder labels and populate the `typeAnnotations` field on the hover result.

#### Scenario: Display parts for typed variable
- **WHEN** hover is extracted for `string greeting`
- **THEN** parts include entries with kinds `punctuation`, `text`, `keyword`, `space`, `localName` mapping to Roslyn's `SymbolDisplayPartKind`

#### Scenario: Display parts for anonymous typed variable
- **WHEN** hover is extracted for `var x = new { Name = "test" };` targeting `x`
- **THEN** parts contain a placeholder `'a` in place of the compiler-generated anonymous type name, and `typeAnnotations` is populated with the expansion
