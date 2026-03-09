## MODIFIED Requirements

### Requirement: Remove marker lines from output
The system SHALL remove all marker lines (`^?` comments, `^|` comments, `@errors` directives, `@noErrors`, cut markers, `@hide`/`@show`, `@highlight`/`@focus`/`@diff` directives, `@langVersion` directives, `@nullable` directives) from the processed output code.

#### Scenario: Clean output code
- **WHEN** source contains markers interspersed with code
- **THEN** the output `code` field contains only the actual C# code with no marker lines

#### Scenario: LangVersion and nullable markers stripped
- **WHEN** source contains `// @langVersion: 12` and `// @nullable: disable` alongside code
- **THEN** both marker lines are removed from the processed output code

### Requirement: Directive markers included in position offset map
When directive marker lines are removed, the position offset map SHALL account for these removals. All output positions (hovers, errors, highlights) SHALL reference the processed code with directive lines removed. This includes `@langVersion` and `@nullable` marker lines.

#### Scenario: Position adjustment after directive removal
- **WHEN** a `// @highlight` line is removed between two code lines
- **THEN** hover, error, and highlight positions in the output reference the adjusted line numbers in the processed code

#### Scenario: Position adjustment after langVersion/nullable removal
- **WHEN** `// @langVersion: 12` and `// @nullable: disable` lines appear before code with `^?` markers
- **THEN** hover positions account for the two removed marker lines
