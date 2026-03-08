## ADDED Requirements

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

### Requirement: Hover objects in JSON
Each hover entry SHALL contain: `line` (number), `character` (number), `length` (number), `text` (string), `parts` (array of `{kind, text}`), `docs` (string or null), `symbolKind` (string), and `targetText` (string).

#### Scenario: Hover JSON shape
- **WHEN** a hover query resolves to a local variable `int x`
- **THEN** the hover object has `line`, `character`, `length` as numbers, `text` as `"(local variable) int x"`, `parts` as an array of kind/text objects, `symbolKind` as `"Local"`, and `targetText` as `"x"`

### Requirement: Error objects in JSON
Each error entry SHALL contain: `line` (number), `character` (number), `length` (number), `code` (string), `message` (string), `severity` (one of `"error"`, `"warning"`, `"info"`, `"hidden"`), and `expected` (boolean).

#### Scenario: Error JSON shape
- **WHEN** compilation produces error CS1002 at line 3, character 8
- **THEN** the error object has `code: "CS1002"`, `severity: "error"`, correct position fields, and `expected` reflecting whether it was declared via `// @errors:`

### Requirement: Meta object in JSON
The `meta` object SHALL contain: `targetFramework` (string), `packages` (array, empty for MVP), and `compileSucceeded` (boolean).

#### Scenario: Successful compilation meta
- **WHEN** compilation succeeds with no unexpected errors
- **THEN** `meta.compileSucceeded` is `true` and `meta.targetFramework` reflects the resolved framework

### Requirement: Empty arrays for unused fields
Fields without data (`completions`, `highlights`, `hidden`) SHALL be present as empty arrays, not omitted.

#### Scenario: No completions in output
- **WHEN** source has no `^|` markers
- **THEN** `completions` is `[]`, not absent from the JSON

### Requirement: Parts kind values
The `kind` field in hover parts SHALL use values mapped from Roslyn's `SymbolDisplayPartKind`: `keyword`, `className`, `structName`, `interfaceName`, `enumName`, `delegateName`, `methodName`, `propertyName`, `fieldName`, `eventName`, `localName`, `parameterName`, `namespaceName`, `punctuation`, `operator`, `space`, `text`, `lineBreak`.

#### Scenario: Parts kind mapping
- **WHEN** Roslyn produces a display part with `SymbolDisplayPartKind.Keyword`
- **THEN** the JSON part has `kind: "keyword"`

### Requirement: Completion objects in JSON
Each completion entry SHALL contain: `line` (number), `character` (number), and `items` (array of completion items). Each item SHALL contain `label` (string), `kind` (string), and `detail` (string or null).

#### Scenario: Completion JSON shape
- **WHEN** a `^|` marker triggers completions at line 2, character 8
- **THEN** the completion object has `line: 2`, `character: 8`, and `items` as an array of `{label, kind, detail}` objects

#### Scenario: Completion item kinds
- **WHEN** completions include methods, properties, and locals
- **THEN** items have `kind` values like `"Method"`, `"Property"`, `"Local"` matching the symbol kind
