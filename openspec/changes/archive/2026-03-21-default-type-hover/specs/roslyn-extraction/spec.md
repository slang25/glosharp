## ADDED Requirements

### Requirement: Extract hovers for all tokens in bulk mode
The system SHALL support extracting hover information for all semantically meaningful tokens in the compilation, in addition to the existing `^?`-targeted extraction. The same symbol resolution logic (`GetSymbolInfo`, `GetDeclaredSymbol`, parent walking) and display formatting SHALL be used for both paths.

#### Scenario: Bulk extraction produces hovers for all identifiers
- **WHEN** source is `var x = 42; Console.WriteLine(x);` with no `^?` markers
- **THEN** the system produces hovers for `x` (declaration), `Console`, `WriteLine`, and `x` (usage)

#### Scenario: Bulk extraction uses same display format as targeted extraction
- **WHEN** source contains `var x = 42;` and both bulk extraction and a `^?` marker target `x`
- **THEN** both produce identical `text`, `parts`, `docs`, and `symbolKind` values

## MODIFIED Requirements

### Requirement: Extract hover information at queried positions
The system SHALL resolve the syntax node at each hover query position, obtain symbol info via `SemanticModel.GetSymbolInfo()` or `SemanticModel.GetDeclaredSymbol()`, and produce structured hover data including display parts, documentation, and symbol kind. The `Docs` field SHALL contain a structured `GloSharpDocComment` object (or null) instead of a plain summary string. Hovers extracted from `^?` markers SHALL have `persistent` set to `true`.

#### Scenario: Local variable hover
- **WHEN** a `^?` hover query targets a local variable `x` of type `int`
- **THEN** the hover text is `(local variable) int x` with structured parts containing keyword, text, and localName kinds, and `persistent` is `true`

#### Scenario: Method call hover
- **WHEN** a `^?` hover query targets `Console.WriteLine`
- **THEN** the hover text shows the method signature with overload count and structured parts, and `persistent` is `true`

#### Scenario: Hover with XML doc comment
- **WHEN** a `^?` hover query targets a symbol that has XML documentation with `<summary>`, `<param>`, and `<returns>` tags
- **THEN** the hover includes a `docs` object with `summary`, `params`, and `returns` fields populated

#### Scenario: Hover with summary-only doc comment
- **WHEN** a `^?` hover query targets a symbol with only a `<summary>` XML doc
- **THEN** the hover includes a `docs` object with `summary` set and `params`, `returns`, `remarks`, `examples`, `exceptions` as empty/null

#### Scenario: Hover without documentation
- **WHEN** a `^?` hover query targets a symbol with no XML documentation
- **THEN** the hover `docs` field is null
