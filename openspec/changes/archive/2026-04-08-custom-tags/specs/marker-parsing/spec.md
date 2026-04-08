## ADDED Requirements

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

## MODIFIED Requirements

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
