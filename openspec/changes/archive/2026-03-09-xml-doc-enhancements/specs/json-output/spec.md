## MODIFIED Requirements

### Requirement: Hover objects in JSON
Each hover entry SHALL contain: `line` (number), `character` (number), `length` (number), `text` (string), `parts` (array of `{kind, text}`), `docs` (structured `GloSharpDocComment` object or null), `symbolKind` (string), and `targetText` (string). The `docs` object, when present, SHALL contain: `summary` (string or null), `params` (array of `{name, text}`), `returns` (string or null), `remarks` (string or null), `examples` (array of strings), and `exceptions` (array of `{type, text}`). Empty arrays and null fields within `docs` SHALL be omitted from JSON output.

#### Scenario: Hover JSON with full docs
- **WHEN** a hover query resolves to a method with summary, params, and returns documentation
- **THEN** the hover object has `docs` as an object with `summary`, `params`, and `returns` fields; empty fields like `examples` and `exceptions` are omitted

#### Scenario: Hover JSON with summary-only docs
- **WHEN** a hover query resolves to a symbol with only a `<summary>` doc comment
- **THEN** the hover object has `docs: { "summary": "..." }` with no other fields present

#### Scenario: Hover JSON without docs
- **WHEN** a hover query resolves to a symbol with no XML documentation
- **THEN** the hover object has `docs: null` (or `docs` is omitted per null serialization settings)

#### Scenario: Hover JSON shape
- **WHEN** a hover query resolves to a local variable `int x`
- **THEN** the hover object has `line`, `character`, `length` as numbers, `text` as `"(local variable) int x"`, `parts` as an array of kind/text objects, `symbolKind` as `"Local"`, and `targetText` as `"x"`
