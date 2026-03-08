## ADDED Requirements

### Requirement: Parse hover query markers
The system SHALL recognize `^?` markers in comment lines to indicate hover queries. The `^` character's column position in the comment SHALL determine which token on the preceding line is queried.

#### Scenario: Single hover query
- **WHEN** source contains `var x = 42;` followed by `//  ^?` where `^` aligns with column 4
- **THEN** the system records a hover query targeting the token at line 0, character 4

#### Scenario: Multiple hover queries
- **WHEN** source contains multiple `^?` marker lines at different positions
- **THEN** the system records one hover query per marker, each targeting the correct line and column

### Requirement: Parse error expectation markers
The system SHALL recognize `// @errors: NNNN` directives to declare expected compiler errors on the following line. Multiple error codes SHALL be supported as a comma-separated list.

#### Scenario: Single expected error
- **WHEN** a line contains `// @errors: CS1002`
- **THEN** the system records that error CS1002 is expected on the next code line

#### Scenario: Multiple expected errors
- **WHEN** a line contains `// @errors: CS1002, CS0246`
- **THEN** the system records both CS1002 and CS0246 as expected on the next code line

### Requirement: Parse noErrors directive
The system SHALL recognize `// @noErrors` to assert that the snippet compiles without any diagnostics at error severity.

#### Scenario: Clean compilation assertion
- **WHEN** source contains `// @noErrors` and compilation produces no errors
- **THEN** processing succeeds

#### Scenario: Unexpected error with noErrors
- **WHEN** source contains `// @noErrors` but compilation produces an error
- **THEN** processing fails with an error indicating the unexpected diagnostic

### Requirement: Parse cut markers
The system SHALL recognize `// ---cut---` to split source into visible and hidden sections. Code before the first cut marker SHALL be hidden from output but included in compilation.

#### Scenario: Setup code hidden by cut
- **WHEN** source contains setup code followed by `// ---cut---` followed by display code
- **THEN** the output `code` contains only the display code, but compilation includes all code

### Requirement: Parse hide/show directives
The system SHALL recognize `// @hide` and `// @show` to toggle visibility of code sections. Lines between `// @hide` and `// @show` (or end of file) SHALL be excluded from output but included in compilation.

#### Scenario: Hidden middle section
- **WHEN** source contains visible code, then `// @hide`, then hidden code, then `// @show`, then more visible code
- **THEN** the output `code` excludes the hidden section but compilation includes it

### Requirement: Remove marker lines from output
The system SHALL remove all marker lines (`^?` comments, `@errors` directives, `@noErrors`, cut markers, `@hide`/`@show`) from the processed output code.

#### Scenario: Clean output code
- **WHEN** source contains markers interspersed with code
- **THEN** the output `code` field contains only the actual C# code with no marker lines

### Requirement: Build position offset map
The system SHALL maintain a mapping from processed-code line numbers to original-code line numbers after removing marker lines. All output positions (hovers, errors) SHALL reference the processed code.

#### Scenario: Position adjustment after marker removal
- **WHEN** a `^?` marker line is removed between two code lines
- **THEN** hover and error positions in the output reference the adjusted line numbers in the processed code
