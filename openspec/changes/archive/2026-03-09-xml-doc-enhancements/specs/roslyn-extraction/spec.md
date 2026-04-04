## MODIFIED Requirements

### Requirement: Extract hover information at queried positions
The system SHALL resolve the syntax node at each hover query position, obtain symbol info via `SemanticModel.GetSymbolInfo()` or `SemanticModel.GetDeclaredSymbol()`, and produce structured hover data including display parts, documentation, and symbol kind. The `Docs` field SHALL contain a structured `GloSharpDocComment` object (or null) instead of a plain summary string.

#### Scenario: Local variable hover
- **WHEN** a hover query targets a local variable `x` of type `int`
- **THEN** the hover text is `(local variable) int x` with structured parts containing keyword, text, and localName kinds

#### Scenario: Method call hover
- **WHEN** a hover query targets `Console.WriteLine`
- **THEN** the hover text shows the method signature with overload count and structured parts

#### Scenario: Hover with XML doc comment
- **WHEN** a hover query targets a symbol that has XML documentation with `<summary>`, `<param>`, and `<returns>` tags
- **THEN** the hover includes a `docs` object with `summary`, `params`, and `returns` fields populated

#### Scenario: Hover with summary-only doc comment
- **WHEN** a hover query targets a symbol with only a `<summary>` XML doc
- **THEN** the hover includes a `docs` object with `summary` set and `params`, `returns`, `remarks`, `examples`, `exceptions` as empty/null

#### Scenario: Hover without documentation
- **WHEN** a hover query targets a symbol with no XML documentation
- **THEN** the hover `docs` field is null
