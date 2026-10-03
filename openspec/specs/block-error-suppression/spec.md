# block-error-suppression Specification

## Purpose
Suppress all, or specific, diagnostics for a snippet with `@suppressErrors` / `@noErrors`.

## Requirements
### Requirement: Parse suppressErrors directive for all errors
The system SHALL recognize `// @suppressErrors` as a block-level directive that suppresses all compiler diagnostics for the code block — errors, warnings and info alike, including errors in hidden (cut/region) code and unmatched `@errors` expectations (GS0003). `compileSucceeded` stays true. The directive line SHALL be removed from processed output and excluded from compilation code.

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
The system SHALL recognize `// @suppressErrors: CS0246, CS0103` as a block-level directive that suppresses only the specified error codes across the entire block. Multiple codes SHALL be supported, separated by commas and/or whitespace. The directive line SHALL be removed from processed output.

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
The system SHALL support both `@suppressErrors` (block-level) and `@errors` (per-line) in the same code block. Per-line expectations are matched first (so a suppressed diagnostic still satisfies its `@errors` line), then block-level suppression removes the suppressed codes from the output.

#### Scenario: Block suppression with per-line errors
- **WHEN** source contains `// @suppressErrors: CS0246` and also has `// @errors: CS1002` on a specific line
- **THEN** CS0246 is suppressed block-wide and CS1002 is expected on that specific line

### Requirement: noErrors is an alias for suppressErrors
The system SHALL treat `@noErrors` as a twoslash-compatible alias for `@suppressErrors`. Both directives MAY be present simultaneously without conflict.

#### Scenario: noErrors suppresses warnings too
- **WHEN** source contains `// @noErrors` and the code produces a CS0219 warning and a CS0103 error
- **THEN** neither is reported and `compileSucceeded` is true

This matches twoslash, where `@noErrors` hides every diagnostic. (Earlier wording that limited suppression to error-severity diagnostics did not match the implementation and has been corrected.)

#### Scenario: noErrors suppresses all errors
- **WHEN** source contains `// @noErrors` and the code has a CS0029 error
- **THEN** no errors are reported, exactly as with a bare `// @suppressErrors`
