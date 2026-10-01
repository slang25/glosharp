## ADDED Requirements

### Requirement: Extract completions at queried positions using CompletionService
The system SHALL use an `AdhocWorkspace` with `CompletionService.GetCompletionsAsync()` to extract completion items at each `^|` marker position. The workspace SHALL reuse the same MetadataReferences already resolved for compilation.

#### Scenario: Completions after member access
- **WHEN** source contains `Console.` followed by `//      ^|` with `^` aligned to the dot position
- **THEN** the system returns completion items including `WriteLine`, `Write`, `ReadLine`, etc., each with label, kind, and detail

#### Scenario: Completions for local variables in scope
- **WHEN** source defines `var name = "test";` and a later line has a `^|` marker inside an expression
- **THEN** the completion items include `name` with kind `Local`

#### Scenario: No completions at invalid position
- **WHEN** a `^|` marker points to a position inside a string literal
- **THEN** the system returns an empty completion items list for that position and adds a `meta.warnings` entry

#### Scenario: Caret past the end of its line
- **WHEN** a `^|` caret's column is greater than the target line's length
- **THEN** the query is skipped and a `meta.warnings` entry is added (a caret exactly at the end of the line is valid)

### Requirement: Filter and deduplicate completion items
Items SHALL be filtered by the identifier already typed before the caret (the completion span's text), case-insensitively by prefix, and SHALL be deduplicated by label (overloads and generic/non-generic variants appear once).

#### Scenario: Typed prefix
- **WHEN** source contains `sb.App` with a `^|` after `App`
- **THEN** only items starting with `App` (`Append`, `AppendFormat`, `AppendJoin`, `AppendLine`) are returned

### Requirement: Completion items include label, kind, and detail
Each completion item SHALL include `label` (the display text), `kind` (symbol kind string such as `"Method"`, `"Property"`, `"Local"`), and `detail` (optional type signature or description).

#### Scenario: Method completion item
- **WHEN** completions include `Console.WriteLine`
- **THEN** the item has `label: "WriteLine"`, `kind: "Method"`, and a `detail` string describing the method signature

#### Scenario: Property completion item
- **WHEN** completions include a property like `Length` on a string
- **THEN** the item has `label: "Length"`, `kind: "Property"`, and `detail` showing the return type

### Requirement: Process method is async
The `GloSharpProcessor.Process()` method SHALL be async (returning `Task<GloSharpResult>`) to support the async `CompletionService.GetCompletionsAsync()` API.

#### Scenario: Async processing with completions
- **WHEN** source contains `^|` markers
- **THEN** the processor awaits completion extraction and returns the full result

#### Scenario: Async processing without completions
- **WHEN** source contains only `^?` markers and no `^|` markers
- **THEN** the processor completes without creating an AdhocWorkspace (no unnecessary overhead)
