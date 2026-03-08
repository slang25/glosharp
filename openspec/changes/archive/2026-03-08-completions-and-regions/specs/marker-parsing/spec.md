## ADDED Requirements

### Requirement: Parse completion query markers
The system SHALL recognize `^|` markers in comment lines to indicate completion queries. The `^` character's column position in the comment SHALL determine the position on the preceding line where completions are queried.

#### Scenario: Single completion query
- **WHEN** source contains `Console.` followed by `//      ^|` where `^` aligns with the position after the dot
- **THEN** the system records a completion query targeting the token at the correct line and character

#### Scenario: Multiple completion queries
- **WHEN** source contains multiple `^|` marker lines at different positions
- **THEN** the system records one completion query per marker, each targeting the correct line and column

#### Scenario: Completion markers removed from output
- **WHEN** source contains `^|` marker lines
- **THEN** the marker lines are removed from the processed output code and positions are adjusted accordingly
