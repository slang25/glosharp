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
- **THEN** the system returns an empty completion items list for that position

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
