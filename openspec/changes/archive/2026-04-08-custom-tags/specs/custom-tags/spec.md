## ADDED Requirements

### Requirement: Parse custom tag directives
The system SHALL recognize `// @log: <message>`, `// @warn: <message>`, `// @error: <message>`, and `// @annotate: <message>` comment lines as custom tag directives. The tag name SHALL be extracted from the directive and the message SHALL be the trimmed text following the colon. A directive without a colon or without message text after the colon SHALL NOT be treated as a custom tag.

#### Scenario: Log tag parsed
- **WHEN** source contains `// @log: This value is cached`
- **THEN** the system records a tag with `name: "log"` and `text: "This value is cached"`

#### Scenario: Warn tag parsed
- **WHEN** source contains `// @warn: This API is deprecated`
- **THEN** the system records a tag with `name: "warn"` and `text: "This API is deprecated"`

#### Scenario: Error tag parsed
- **WHEN** source contains `// @error: This will throw at runtime`
- **THEN** the system records a tag with `name: "error"` and `text: "This will throw at runtime"`

#### Scenario: Annotate tag parsed
- **WHEN** source contains `// @annotate: Note the use of pattern matching here`
- **THEN** the system records a tag with `name: "annotate"` and `text: "Note the use of pattern matching here"`

#### Scenario: Bare tag without message ignored
- **WHEN** source contains `// @log` with no colon or message
- **THEN** the line is NOT treated as a custom tag directive

#### Scenario: Tag with empty message ignored
- **WHEN** source contains `// @log:` with nothing after the colon (or only whitespace)
- **THEN** the line is NOT treated as a custom tag directive

### Requirement: Custom tags target the preceding code line
Each custom tag SHALL be associated with the processed output line immediately preceding the tag directive. The tag's `line` field SHALL be the 0-based line number of that preceding code line in the processed output.

#### Scenario: Tag after code line
- **WHEN** source contains `var x = 42;` on processed line 2 followed by `// @log: x is set`
- **THEN** the tag has `line: 2`

#### Scenario: Tag on first line
- **WHEN** source starts with `// @log: Entry point` before any code lines
- **THEN** the tag has `line: 0` (targeting the first output line)

#### Scenario: Multiple tags on same line
- **WHEN** source contains `var x = 42;` followed by `// @log: message one` and `// @warn: message two`
- **THEN** both tags have the same `line` value targeting the line containing `var x = 42;`

### Requirement: Custom tag lines stripped from output
Custom tag directive lines SHALL be removed from the processed output code, consistent with other marker lines.

#### Scenario: Tag stripped from output
- **WHEN** source contains `var x = 42;` followed by `// @log: cached value`
- **THEN** the processed output code contains `var x = 42;` but not the `// @log:` line

### Requirement: Custom tag lines excluded from compilation
Custom tag directive lines SHALL be excluded from the code sent to the compiler, consistent with other directive markers.

#### Scenario: Tag excluded from compilation
- **WHEN** source contains `// @annotate: This is a note` between code lines
- **THEN** the compilation code does not include the tag line

### Requirement: GloSharpTag model
Each tag entry SHALL contain: `name` (string, one of `"log"`, `"warn"`, `"error"`, `"annotate"`), `text` (string, the message content), and `line` (number, 0-based processed output line).

#### Scenario: Tag entry shape
- **WHEN** source contains `var x = 42;` followed by `// @warn: Potential null reference`
- **THEN** the tag entry has `name: "warn"`, `text: "Potential null reference"`, and `line` as the 0-based processed line number of `var x = 42;`

### Requirement: Custom tags coexist with other markers
Custom tag directives SHALL work alongside existing markers (`^?`, `@errors`, `@highlight`, `---cut---`, etc.) without interference. Position mapping SHALL account for all removed marker lines including tag lines.

#### Scenario: Tag with hover query
- **WHEN** source contains a `^?` hover marker and a `// @log:` tag on the same code block
- **THEN** both the hover query and the tag are correctly positioned in the processed output

#### Scenario: Tag after cut marker
- **WHEN** source contains `// ---cut---` followed by code and a `// @annotate:` tag
- **THEN** the tag targets the correct line in the processed output (accounting for cut-hidden lines)
