## ADDED Requirements

### Requirement: Parse suppressErrors directive for all errors
The system SHALL recognize `// @suppressErrors` as a block-level directive that suppresses all compilation errors for the code block. The directive line SHALL be removed from processed output and excluded from compilation code.

#### Scenario: Suppress all errors directive parsed
- **WHEN** source contains `// @suppressErrors` on its own line
- **THEN** the marker parse result indicates that all errors should be suppressed, and the directive line is removed from processed output

#### Scenario: Suppress all errors with compilation errors present
- **WHEN** source contains `// @suppressErrors` and the code has CS0246 and CS0103 errors
- **THEN** processing succeeds with no errors reported in the output

#### Scenario: Suppress all errors still extracts hovers
- **WHEN** source contains `// @suppressErrors` and the code has some resolvable symbols alongside errors
- **THEN** hovers are extracted for the resolvable symbols and no errors are reported

### Requirement: Parse suppressErrors directive with specific codes
The system SHALL recognize `// @suppressErrors: CS0246, CS0103` as a block-level directive that suppresses only the specified error codes across the entire block. Multiple codes SHALL be supported as a comma-separated list. The directive line SHALL be removed from processed output.

#### Scenario: Suppress specific error codes block-wide
- **WHEN** source contains `// @suppressErrors: CS0246` and the code has CS0246 errors on multiple lines
- **THEN** all CS0246 errors are suppressed and not reported in the output

#### Scenario: Non-suppressed errors still reported
- **WHEN** source contains `// @suppressErrors: CS0246` and the code has both CS0246 and CS1002 errors
- **THEN** CS0246 errors are suppressed but CS1002 errors are reported normally

#### Scenario: Multiple suppressed codes
- **WHEN** source contains `// @suppressErrors: CS0246, CS0103, CS1729`
- **THEN** all three error codes are suppressed block-wide

### Requirement: suppressErrors coexists with per-line @errors
The system SHALL support both `@suppressErrors` (block-level) and `@errors` (per-line) in the same code block. Block-level suppression applies first, then per-line expectations apply to any remaining errors.

#### Scenario: Block suppression with per-line errors
- **WHEN** source contains `// @suppressErrors: CS0246` and also has `// @errors: CS1002` on a specific line
- **THEN** CS0246 is suppressed block-wide and CS1002 is expected on that specific line

### Requirement: suppressErrors is incompatible with noErrors
The system SHALL treat `@suppressErrors` and `@noErrors` as mutually exclusive. If both are present, the system SHALL report an error indicating the conflict.

#### Scenario: Conflicting directives
- **WHEN** source contains both `// @suppressErrors` and `// @noErrors`
- **THEN** processing fails with an error indicating that @suppressErrors and @noErrors cannot be used together
