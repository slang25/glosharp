## MODIFIED Requirements

### Requirement: Top-level JSON structure
The system SHALL output JSON with the following top-level fields: `code` (processed source), `original` (source with markers), `lang` (always `"csharp"`), `hovers`, `errors`, `completions`, `highlights`, `hidden`, and `meta`. The `completions` array SHALL contain structured completion objects when `^|` markers are present.

#### Scenario: Complete output structure
- **WHEN** a C# snippet with hover markers is processed
- **THEN** the JSON output contains all top-level fields, with `code` having markers removed and `original` preserving the input

#### Scenario: Output with completions
- **WHEN** a C# snippet with `^|` markers is processed
- **THEN** the `completions` array contains completion objects with `line`, `character`, and `items` fields

#### Scenario: Output without completions
- **WHEN** a C# snippet with no `^|` markers is processed
- **THEN** the `completions` array is `[]`

## ADDED Requirements

### Requirement: Completion objects in JSON
Each completion entry SHALL contain: `line` (number), `character` (number), and `items` (array of completion items). Each item SHALL contain `label` (string), `kind` (string), and `detail` (string or null).

#### Scenario: Completion JSON shape
- **WHEN** a `^|` marker triggers completions at line 2, character 8
- **THEN** the completion object has `line: 2`, `character: 8`, and `items` as an array of `{label, kind, detail}` objects

#### Scenario: Completion item kinds
- **WHEN** completions include methods, properties, and locals
- **THEN** items have `kind` values like `"Method"`, `"Property"`, `"Local"` matching the symbol kind
