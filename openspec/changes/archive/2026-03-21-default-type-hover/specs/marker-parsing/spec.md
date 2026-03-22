## MODIFIED Requirements

### Requirement: Parse hover query markers
The system SHALL recognize `^?` markers in comment lines to indicate persistent hover requests. The `^` character's column position in the comment SHALL determine which token on the preceding line is targeted for a persistent (always-visible) hover display.

#### Scenario: Single persistent hover marker
- **WHEN** source contains `var x = 42;` followed by `//  ^?` where `^` aligns with column 4
- **THEN** the system records a persistent hover request targeting the token at line 0, character 4

#### Scenario: Multiple persistent hover markers
- **WHEN** source contains multiple `^?` marker lines at different positions
- **THEN** the system records one persistent hover request per marker, each targeting the correct line and column
