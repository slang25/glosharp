## MODIFIED Requirements

### Requirement: Error objects in JSON
Each error entry SHALL contain: `line` (number), `character` (number), `length` (number), `code` (string), `message` (string), `severity` (one of `"error"`, `"warning"`, `"info"`, `"hidden"`), and `expected` (boolean). When a diagnostic spans multiple lines, the error entry SHALL also contain `endLine` (number) and `endCharacter` (number). These fields SHALL be omitted when the diagnostic is single-line.

#### Scenario: Error JSON shape
- **WHEN** compilation produces error CS1002 at line 3, character 8
- **THEN** the error object has `code: "CS1002"`, `severity: "error"`, correct position fields, and `expected` reflecting whether it was declared via `// @errors:`

#### Scenario: Multi-line error JSON shape
- **WHEN** compilation produces a diagnostic spanning from line 3, character 10 to line 5, character 8
- **THEN** the error object includes `endLine: 5` and `endCharacter: 8` in addition to the standard fields

#### Scenario: Single-line error omits end fields
- **WHEN** compilation produces a single-line diagnostic
- **THEN** the error object does not contain `endLine` or `endCharacter` fields
