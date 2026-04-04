## MODIFIED Requirements

### Requirement: Empty arrays for unused fields
Fields without data (`completions`, `highlights`, `hidden`) SHALL be present as empty arrays, not omitted. When directive markers are present, the `highlights` array SHALL contain `GloSharpHighlight` objects instead of being empty.

#### Scenario: No completions in output
- **WHEN** source has no `^|` markers
- **THEN** `completions` is `[]`, not absent from the JSON

#### Scenario: No highlights in output
- **WHEN** source has no `@highlight`, `@focus`, or `@diff` markers
- **THEN** `highlights` is `[]`, not absent from the JSON

#### Scenario: Highlights populated from directives
- **WHEN** source has `@highlight`, `@focus`, or `@diff` markers
- **THEN** `highlights` contains `GloSharpHighlight` objects with `line`, `character`, `length`, and `kind` fields

## ADDED Requirements

### Requirement: Highlight objects in JSON
Each highlight entry SHALL contain: `line` (number, 0-based), `character` (number), `length` (number), and `kind` (string, one of `"highlight"`, `"focus"`, `"add"`, `"remove"`).

#### Scenario: Highlight JSON shape
- **WHEN** a `// @highlight` directive targets a code line
- **THEN** the highlight object has `line` as a 0-based number, `character: 0`, `length` as the line content length, and `kind: "highlight"`

#### Scenario: Diff add JSON shape
- **WHEN** a `// @diff: +` directive targets a code line
- **THEN** the highlight object has `kind: "add"` with correct position fields

#### Scenario: Focus JSON shape
- **WHEN** a `// @focus` directive targets a code line
- **THEN** the highlight object has `kind: "focus"` with correct position fields
