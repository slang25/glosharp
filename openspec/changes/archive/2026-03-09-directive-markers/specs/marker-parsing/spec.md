## ADDED Requirements

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
When directive marker lines are removed, the position offset map SHALL account for these removals. All output positions (hovers, errors, highlights) SHALL reference the processed code with directive lines removed.

#### Scenario: Position adjustment after directive removal
- **WHEN** a `// @highlight` line is removed between two code lines
- **THEN** hover, error, and highlight positions in the output reference the adjusted line numbers in the processed code
