## ADDED Requirements

### Requirement: Parse highlight directive
The system SHALL recognize `// @highlight` comment lines to mark lines for visual highlighting. When used without arguments, the directive SHALL target the next code line. When used with a line range (`// @highlight: N` or `// @highlight: N-M`), it SHALL target the specified output lines (1-based).

#### Scenario: Highlight next line
- **WHEN** source contains `// @highlight` followed by `var x = 42;`
- **THEN** the system records a highlight entry with `kind: "highlight"` targeting the processed line containing `var x = 42;`

#### Scenario: Highlight single line by number
- **WHEN** source contains `// @highlight: 3`
- **THEN** the system records a highlight entry with `kind: "highlight"` targeting processed output line 3 (1-based)

#### Scenario: Highlight line range
- **WHEN** source contains `// @highlight: 3-5`
- **THEN** the system records three highlight entries with `kind: "highlight"` targeting processed output lines 3, 4, and 5

#### Scenario: Highlight directive stripped from output
- **WHEN** source contains `// @highlight` lines
- **THEN** the directive lines are removed from the processed output code

### Requirement: Parse focus directive
The system SHALL recognize `// @focus` comment lines to mark lines for focus presentation (non-focused lines are dimmed). When used without arguments, the directive SHALL target the next code line. When used with a line range (`// @focus: N` or `// @focus: N-M`), it SHALL target the specified output lines (1-based).

#### Scenario: Focus next line
- **WHEN** source contains `// @focus` followed by `var x = 42;`
- **THEN** the system records a highlight entry with `kind: "focus"` targeting the processed line containing `var x = 42;`

#### Scenario: Focus line range
- **WHEN** source contains `// @focus: 2-4`
- **THEN** the system records three highlight entries with `kind: "focus"` targeting processed output lines 2, 3, and 4

#### Scenario: Focus directive stripped from output
- **WHEN** source contains `// @focus` lines
- **THEN** the directive lines are removed from the processed output code

### Requirement: Parse diff directive
The system SHALL recognize `// @diff: +` and `// @diff: -` comment lines to mark the next code line as added or removed for diff presentation.

#### Scenario: Diff add marker
- **WHEN** source contains `// @diff: +` followed by `var x = 42;`
- **THEN** the system records a highlight entry with `kind: "add"` targeting the processed line containing `var x = 42;`

#### Scenario: Diff remove marker
- **WHEN** source contains `// @diff: -` followed by `var x = 42;`
- **THEN** the system records a highlight entry with `kind: "remove"` targeting the processed line containing `var x = 42;`

#### Scenario: Diff directive stripped from output
- **WHEN** source contains `// @diff: +` or `// @diff: -` lines
- **THEN** the directive lines are removed from the processed output code

### Requirement: TwohashHighlight model
Each highlight entry SHALL contain: `line` (number, 0-based in processed output), `character` (number, 0 for whole-line), `length` (number, length of the line content for whole-line), and `kind` (one of `"highlight"`, `"focus"`, `"add"`, `"remove"`).

#### Scenario: Highlight entry shape
- **WHEN** a `// @highlight` directive targets a line containing `var x = 42;`
- **THEN** the highlight entry has `line` as the 0-based processed line number, `character: 0`, `length` equal to the line content length, and `kind: "highlight"`

### Requirement: Directives work alongside existing markers
Directive markers SHALL coexist with existing markers (`^?`, `@errors`, `@hide`, etc.) without interference. Position mapping SHALL account for all removed marker lines.

#### Scenario: Highlight with hover query
- **WHEN** source contains both `// @highlight` and `//  ^?` markers
- **THEN** both the highlight entry and hover query are correctly positioned in the processed output

#### Scenario: Highlight after cut marker
- **WHEN** source contains `// ---cut---` followed by `// @highlight` and code
- **THEN** the highlight entry targets the correct line in the processed output (accounting for cut-hidden lines)
