## ADDED Requirements

### Requirement: Tag objects in JSON
Each tag entry SHALL contain: `name` (string, one of `"log"`, `"warn"`, `"error"`, `"annotate"`), `text` (string, the message content), and `line` (number, 0-based processed output line).

#### Scenario: Tag JSON shape
- **WHEN** source contains `var x = 42;` followed by `// @log: x is assigned`
- **THEN** the tag object has `name: "log"`, `text: "x is assigned"`, and `line` as the 0-based processed line number

#### Scenario: Multiple tags in output
- **WHEN** source contains `// @warn: deprecated` and `// @annotate: use v2 instead` on different lines
- **THEN** the `tags` array contains two entries with correct name, text, and line values

## MODIFIED Requirements

### Requirement: Top-level JSON structure
The system SHALL output JSON with the following top-level fields: `code` (processed source), `original` (source with markers), `lang` (always `"csharp"`), `hovers`, `errors`, `completions`, `highlights`, `tags`, `hidden`, and `meta`. The `completions` array SHALL contain structured completion objects when `^|` markers are present.

#### Scenario: Complete output structure
- **WHEN** a C# snippet with hover markers is processed
- **THEN** the JSON output contains all top-level fields, with `code` having markers removed and `original` preserving the input

#### Scenario: Output with completions
- **WHEN** a C# snippet with `^|` markers is processed
- **THEN** the `completions` array contains completion objects with `line`, `character`, and `items` fields

#### Scenario: Output without completions
- **WHEN** a C# snippet with no `^|` markers is processed
- **THEN** the `completions` array is `[]`

#### Scenario: Output with tags
- **WHEN** a C# snippet with `// @log: message` is processed
- **THEN** the `tags` array contains tag objects with `name`, `text`, and `line` fields

### Requirement: Empty arrays for unused fields
Fields without data (`completions`, `highlights`, `tags`, `hidden`) SHALL be present as empty arrays, not omitted. When directive markers are present, the `highlights` array SHALL contain `GloSharpHighlight` objects instead of being empty. When custom tag directives are present, the `tags` array SHALL contain `GloSharpTag` objects instead of being empty.

#### Scenario: No completions in output
- **WHEN** source has no `^|` markers
- **THEN** `completions` is `[]`, not absent from the JSON

#### Scenario: No highlights in output
- **WHEN** source has no `@highlight`, `@focus`, or `@diff` markers
- **THEN** `highlights` is `[]`, not absent from the JSON

#### Scenario: Highlights populated from directives
- **WHEN** source has `@highlight`, `@focus`, or `@diff` markers
- **THEN** `highlights` contains `GloSharpHighlight` objects with `line`, `character`, `length`, and `kind` fields

#### Scenario: No tags in output
- **WHEN** source has no `@log`, `@warn`, `@error`, or `@annotate` tag directives
- **THEN** `tags` is `[]`, not absent from the JSON

#### Scenario: Tags populated from directives
- **WHEN** source has `@log`, `@warn`, `@error`, or `@annotate` tag directives
- **THEN** `tags` contains `GloSharpTag` objects with `name`, `text`, and `line` fields
