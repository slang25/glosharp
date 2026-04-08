## ADDED Requirements

### Requirement: Parse hover query markers
The system SHALL recognize `^?` markers in comment lines to indicate persistent hover requests. The `^` character's column position in the comment SHALL determine which token on the preceding line is targeted for a persistent (always-visible) hover display.

#### Scenario: Single persistent hover marker
- **WHEN** source contains `var x = 42;` followed by `//  ^?` where `^` aligns with column 4
- **THEN** the system records a persistent hover request targeting the token at line 0, character 4

#### Scenario: Multiple persistent hover markers
- **WHEN** source contains multiple `^?` marker lines at different positions
- **THEN** the system records one persistent hover request per marker, each targeting the correct line and column

### Requirement: Parse error expectation markers
The system SHALL recognize `// @errors: NNNN` directives to declare expected compiler errors on the following line. Multiple error codes SHALL be supported as a comma-separated list.

#### Scenario: Single expected error
- **WHEN** a line contains `// @errors: CS1002`
- **THEN** the system records that error CS1002 is expected on the next code line

#### Scenario: Multiple expected errors
- **WHEN** a line contains `// @errors: CS1002, CS0246`
- **THEN** the system records both CS1002 and CS0246 as expected on the next code line

### Requirement: Parse noErrors directive
The system SHALL recognize `// @noErrors` as a twoslash-compatible alias for `// @suppressErrors`. When present, all error-severity diagnostics SHALL be suppressed from the output.

#### Scenario: noErrors suppresses errors
- **WHEN** source contains `// @noErrors` and compilation produces errors
- **THEN** error-severity diagnostics are suppressed and processing succeeds

### Requirement: Parse cut-before markers
The system SHALL recognize `// ---cut---` and `// ---cut-before---` to split source into visible and hidden sections. Code before the first occurrence of either marker SHALL be hidden from output but included in compilation.

#### Scenario: Setup code hidden by ---cut---
- **WHEN** source contains setup code followed by `// ---cut---` followed by display code
- **THEN** the output `code` contains only the display code, but compilation includes all code

#### Scenario: Setup code hidden by ---cut-before---
- **WHEN** source contains setup code followed by `// ---cut-before---` followed by display code
- **THEN** the output `code` contains only the display code, but compilation includes all code

### Requirement: Parse cut-after marker
The system SHALL recognize `// ---cut-after---` to hide all code after the marker from output while including it in compilation.

#### Scenario: Trailing code hidden by ---cut-after---
- **WHEN** source contains display code followed by `// ---cut-after---` followed by trailing code
- **THEN** the output `code` contains only the display code, but compilation includes all code

### Requirement: Parse cut-start/cut-end directives
The system SHALL recognize `// ---cut-start---` and `// ---cut-end---` to toggle visibility of code sections. Lines between `// ---cut-start---` and `// ---cut-end---` (or end of file if unclosed) SHALL be excluded from output but included in compilation. Multiple pairs SHALL be supported.

#### Scenario: Hidden middle section
- **WHEN** source contains visible code, then `// ---cut-start---`, then hidden code, then `// ---cut-end---`, then more visible code
- **THEN** the output `code` excludes the hidden section but compilation includes it

#### Scenario: Multiple hidden sections
- **WHEN** source contains two `// ---cut-start---` / `// ---cut-end---` pairs with visible code between them
- **THEN** the output `code` excludes both hidden sections but compilation includes all code

### Requirement: Remove marker lines from output
The system SHALL remove all marker lines (`^?` comments, `^|` comments, `@errors` directives, `@noErrors`, cut markers (`---cut---`, `---cut-before---`, `---cut-after---`, `---cut-start---`, `---cut-end---`), `@highlight`/`@focus`/`@diff` directives, `@langVersion` directives, `@nullable` directives, `@log`/`@warn`/`@error`/`@annotate` tag directives) from the processed output code.

#### Scenario: Clean output code
- **WHEN** source contains markers interspersed with code
- **THEN** the output `code` field contains only the actual C# code with no marker lines

#### Scenario: LangVersion and nullable markers stripped
- **WHEN** source contains `// @langVersion: 12` and `// @nullable: disable` alongside code
- **THEN** both marker lines are removed from the processed output code

#### Scenario: LangVersion and nullable values normalized
- **WHEN** source contains `// @langVersion: Preview` or `// @nullable: Disable`
- **THEN** the parsed values are lowercased to `"preview"` and `"disable"` respectively

#### Scenario: Custom tag markers stripped
- **WHEN** source contains `// @log: message` and `// @warn: message` alongside code
- **THEN** both tag marker lines are removed from the processed output code

### Requirement: Build position offset map
The system SHALL maintain a mapping from processed-code line numbers to original-code line numbers after removing marker lines. All output positions (hovers, errors) SHALL reference the processed code.

#### Scenario: Position adjustment after marker removal
- **WHEN** a `^?` marker line is removed between two code lines
- **THEN** hover and error positions in the output reference the adjusted line numbers in the processed code

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

### Requirement: Parse highlight directive markers
The system SHALL recognize `// @highlight` and `// @highlight: <range>` comment lines as marker lines. These lines SHALL be removed from processed output and excluded from compilation code. The `<range>` argument SHALL support single line numbers (`N`) and ranges (`N-M`), both 1-based.

#### Scenario: Highlight directive identified as marker
- **WHEN** a line contains `// @highlight` or `// @highlight: 3-5`
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

### Requirement: Parse focus directive markers
The system SHALL recognize `// @focus` and `// @focus: <range>` comment lines as marker lines. These lines SHALL be removed from processed output and excluded from compilation code.

#### Scenario: Focus directive identified as marker
- **WHEN** a line contains `// @focus` or `// @focus: 2-4`
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

### Requirement: Parse diff directive markers
The system SHALL recognize `// @diff: +` and `// @diff: -` comment lines as marker lines. These lines SHALL be removed from processed output and excluded from compilation code.

#### Scenario: Diff directive identified as marker
- **WHEN** a line contains `// @diff: +` or `// @diff: -`
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

### Requirement: Directive markers included in position offset map
When directive marker lines are removed, the position offset map SHALL account for these removals. All output positions (hovers, errors, highlights) SHALL reference the processed code with directive lines removed. This includes `@langVersion` and `@nullable` marker lines.

#### Scenario: Position adjustment after directive removal
- **WHEN** a `// @highlight` line is removed between two code lines
- **THEN** hover, error, and highlight positions in the output reference the adjusted line numbers in the processed code

#### Scenario: Position adjustment after langVersion/nullable removal
- **WHEN** `// @langVersion: 12` and `// @nullable: disable` lines appear before code with `^?` markers
- **THEN** hover positions account for the two removed marker lines

### Requirement: Parse file-based app directive lines
The system SHALL recognize lines starting with `#:` as directive marker lines. These lines SHALL be removed from processed output and excluded from compilation code. The position offset map SHALL account for removed `#:` lines.

#### Scenario: Directive line identified as marker
- **WHEN** a line starts with `#:` (e.g., `#:package Newtonsoft.Json@13.0.3`)
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

#### Scenario: Position adjustment after directive removal
- **WHEN** two `#:` directive lines precede code starting at source line 3
- **THEN** hover and error positions in the output reference line 0 for the first code line

#### Scenario: Directives coexist with comment markers
- **WHEN** source contains `#:package` directives followed by code with `// ^?` hover queries
- **THEN** both directive stripping and hover query extraction produce correct positions in the processed output

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

### Requirement: Parse custom tag directive markers
The system SHALL recognize `// @log: <message>`, `// @warn: <message>`, `// @error: <message>`, and `// @annotate: <message>` comment lines as marker lines. These lines SHALL be removed from processed output and excluded from compilation code. A tag directive SHALL only be recognized when it contains a colon followed by non-empty message text.

#### Scenario: Custom tag directive identified as marker
- **WHEN** a line contains `// @log: This is informational`
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

#### Scenario: All four tag types recognized
- **WHEN** lines contain `// @log: msg`, `// @warn: msg`, `// @error: msg`, `// @annotate: msg`
- **THEN** all four lines are treated as marker lines

#### Scenario: Tag without message not treated as marker
- **WHEN** a line contains `// @log` with no colon or message
- **THEN** the line is NOT treated as a custom tag marker (it remains in the output as a regular comment)

### Requirement: Custom tag markers included in position offset map
When custom tag marker lines are removed, the position offset map SHALL account for these removals. All output positions (hovers, errors, highlights, tags) SHALL reference the processed code with tag lines removed.

#### Scenario: Position adjustment after tag removal
- **WHEN** a `// @log: message` line is removed between two code lines
- **THEN** hover, error, highlight, and tag positions in the output reference the adjusted line numbers in the processed code
