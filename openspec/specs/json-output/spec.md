## ADDED Requirements

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

### Requirement: Hover objects in JSON
Each hover entry SHALL contain: `line` (number), `character` (number), `length` (number), `text` (string), `parts` (array of `{kind, text}`), `docs` (structured `GloSharpDocComment` object or null), `symbolKind` (string), and `targetText` (string). The `persistent` field SHALL be omitted from JSON output when `false` (via `JsonIgnore WhenWritingDefault`). The `docs` object, when present, SHALL contain: `summary` (string or null), `params` (array of `{name, text}`), `returns` (string or null), `remarks` (string or null), `examples` (array of strings), and `exceptions` (array of `{type, text}`). Empty arrays within `docs` (e.g., `params`, `examples`, `exceptions`) MAY be present in JSON output as `[]`.

#### Scenario: Hover JSON with full docs
- **WHEN** a hover query resolves to a method with summary, params, and returns documentation
- **THEN** the hover object has `docs` as an object with `summary`, `params`, and `returns` fields; empty fields like `examples` and `exceptions` are omitted

#### Scenario: Hover JSON with summary-only docs
- **WHEN** a hover query resolves to a symbol with only a `<summary>` doc comment
- **THEN** the hover object has `docs: { "summary": "..." }` with no other fields present

#### Scenario: Hover JSON without docs
- **WHEN** a hover query resolves to a symbol with no XML documentation
- **THEN** the hover object has `docs: null` (or `docs` is omitted per null serialization settings)

#### Scenario: Hover JSON shape
- **WHEN** a hover query resolves to a local variable `int x`
- **THEN** the hover object has `line`, `character`, `length` as numbers, `text` as `"(local variable) int x"`, `parts` as an array of kind/text objects, `symbolKind` as `"Local"`, and `targetText` as `"x"`

### Requirement: Error objects in JSON
Each error entry SHALL contain: `line` (number), `character` (number), `length` (number), `code` (string), `message` (string), `severity` (one of `"error"`, `"warning"`, `"info"`, `"hidden"`), and `expected` (boolean). When a diagnostic spans multiple lines, the error entry SHALL also contain `endLine` (number) and `endCharacter` (number). These fields SHALL be omitted when the diagnostic is single-line.

#### Scenario: Error JSON shape
- **WHEN** compilation produces error CS1002 at line 3, character 8
- **THEN** the error object has `code: "CS1002"`, `severity: "error"`, correct position fields, and `expected` reflecting whether it was declared via `// @errors:`

#### Scenario: Multi-line error JSON shape
- **WHEN** compilation produces a diagnostic spanning from line 3, character 10 to line 5, character 8
- **THEN** the error object includes `endLine: 5` and `endCharacter: 8` in addition to the standard fields

#### Scenario: Single-line error omits end fields
- **WHEN** compilation produces a single-line diagnostic
- **THEN** the error object does not contain `endLine` or `endCharacter` fields

### Requirement: Meta object in JSON
The `meta` object SHALL contain: `targetFramework` (string), `packages` (array of `{name, version}` objects), `compileSucceeded` (boolean), `sdk` (string or null), `langVersion` (string or null), and `nullable` (string or null). The `packages` array SHALL be populated from `#:package` directives when present, or from `project.assets.json` when using project-based resolution. The `sdk` field SHALL contain the SDK identifier from `#:sdk` directive, or null when not specified. The `langVersion` field SHALL contain the authored language version string when a `// @langVersion` marker is present, or null when using the default. The `nullable` field SHALL contain the authored nullable context string when a `// @nullable` marker is present, or null when using the default.

#### Scenario: Successful compilation meta
- **WHEN** compilation succeeds with no unexpected errors
- **THEN** `meta.compileSucceeded` is `true` and `meta.targetFramework` reflects the resolved framework

#### Scenario: Meta with file-based app packages
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3` and `#:package Serilog@3.1.1`
- **THEN** `meta.packages` is `[{"name": "Newtonsoft.Json", "version": "13.0.3"}, {"name": "Serilog", "version": "3.1.1"}]`

#### Scenario: Meta with SDK specified
- **WHEN** source contains `#:sdk Microsoft.NET.Sdk.Web`
- **THEN** `meta.sdk` is `"Microsoft.NET.Sdk.Web"`

#### Scenario: Meta without SDK
- **WHEN** source contains no `#:sdk` directive
- **THEN** `meta.sdk` is null or omitted from the JSON output

#### Scenario: Meta with no packages
- **WHEN** source has no `#:package` directives and no project-based resolution
- **THEN** `meta.packages` is `[]`

#### Scenario: Meta with language version
- **WHEN** source contains `// @langVersion: 12`
- **THEN** `meta.langVersion` is `"12"`

#### Scenario: Meta with nullable context
- **WHEN** source contains `// @nullable: disable`
- **THEN** `meta.nullable` is `"disable"`

#### Scenario: Meta without language version or nullable
- **WHEN** source contains no `// @langVersion` or `// @nullable` markers
- **THEN** `meta.langVersion` is null and `meta.nullable` is null (or omitted)

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

### Requirement: Tag objects in JSON
Each tag entry SHALL contain: `name` (string, one of `"log"`, `"warn"`, `"error"`, `"annotate"`), `text` (string, the message content), and `line` (number, 0-based processed output line).

#### Scenario: Tag JSON shape
- **WHEN** source contains `var x = 42;` followed by `// @log: x is assigned`
- **THEN** the tag object has `name: "log"`, `text: "x is assigned"`, and `line` as the 0-based processed line number

#### Scenario: Multiple tags in output
- **WHEN** source contains `// @warn: deprecated` and `// @annotate: use v2 instead` on different lines
- **THEN** the `tags` array contains two entries with correct name, text, and line values
