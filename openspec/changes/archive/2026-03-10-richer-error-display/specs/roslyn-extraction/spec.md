## MODIFIED Requirements

### Requirement: Extract compiler diagnostics
The system SHALL collect all diagnostics from `Compilation.GetDiagnostics()` at error, warning, and info severity levels, with line, character, length, error code, message, and severity. When a diagnostic's source span crosses multiple lines, the system SHALL also extract `endLine` and `endCharacter` from the end of the span's mapped line position.

#### Scenario: Syntax error diagnostic
- **WHEN** source has a missing semicolon
- **THEN** the system produces a diagnostic with code `CS1002`, severity `error`, and the correct position

#### Scenario: Expected vs unexpected errors
- **WHEN** an error matches an `// @errors:` declaration
- **THEN** the diagnostic is marked as `expected: true` in the output

#### Scenario: Multi-line diagnostic span extraction
- **WHEN** a Roslyn diagnostic's source span starts at line 3, character 10 and ends at line 5, character 8
- **THEN** the extracted error has `line: 3`, `character: 10`, `endLine: 5`, `endCharacter: 8`, and `length` reflecting the total span length

#### Scenario: Single-line diagnostic omits end fields
- **WHEN** a Roslyn diagnostic's source span starts and ends on the same line
- **THEN** the extracted error has `line`, `character`, and `length` but `endLine` and `endCharacter` are null/omitted
