## ADDED Requirements

### Requirement: Parse suppressErrors directive
The system SHALL recognize `// @suppressErrors` as a marker line. The directive SHALL set a block-level flag indicating all errors should be suppressed. The marker line SHALL be removed from processed output and excluded from compilation code.

#### Scenario: suppressErrors directive identified as marker
- **WHEN** a line contains `// @suppressErrors`
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

#### Scenario: suppressErrors sets block flag
- **WHEN** source contains `// @suppressErrors`
- **THEN** the parse result has `SuppressAllErrors` set to `true` and `SuppressedErrorCodes` is empty

### Requirement: Parse suppressErrors directive with specific codes
The system SHALL recognize `// @suppressErrors: CS0246, CS0103` as a marker line. The directive SHALL record the specified error codes for block-level suppression. Multiple codes SHALL be supported as a comma-separated list.

#### Scenario: suppressErrors with codes parsed
- **WHEN** a line contains `// @suppressErrors: CS0246, CS0103`
- **THEN** the parse result has `SuppressAllErrors` set to `false` and `SuppressedErrorCodes` contains `["CS0246", "CS0103"]`

#### Scenario: suppressErrors with single code
- **WHEN** a line contains `// @suppressErrors: CS0246`
- **THEN** the parse result has `SuppressedErrorCodes` containing `["CS0246"]`

#### Scenario: suppressErrors directive stripped from output
- **WHEN** source contains `// @suppressErrors: CS0246` alongside code
- **THEN** the directive line is removed from processed output and positions are adjusted accordingly

### Requirement: Position offset map accounts for suppressErrors lines
When `@suppressErrors` lines are removed, the position offset map SHALL account for these removals. All output positions SHALL reference the processed code with the directive line removed.

#### Scenario: Position adjustment after suppressErrors removal
- **WHEN** `// @suppressErrors` appears before code with `^?` markers
- **THEN** hover positions account for the removed marker line
