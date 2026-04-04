## ADDED Requirements

### Requirement: Export map-based transformer factory
The package SHALL export a `transformerGloSharpFromMap(resultMap)` function that accepts a `GloSharpResultMap` and returns a `ShikiTransformer`.

#### Scenario: Create transformer from result map
- **WHEN** `transformerGloSharpFromMap(resultMap)` is called with a populated result map
- **THEN** it returns a Shiki transformer object with `preprocess` and `root` hooks

### Requirement: Preprocess looks up result by code hash
The transformer's `preprocess` hook SHALL compute the SHA256 hash of the incoming code and look it up in the result map. If found, it SHALL return `result.code` (cleaned code with markers removed).

#### Scenario: Code found in map
- **WHEN** the transformer's `preprocess` receives code that was batch-processed
- **THEN** it returns the cleaned code from the corresponding `GloSharpResult`

#### Scenario: Code not found in map
- **WHEN** the transformer's `preprocess` receives code that was not batch-processed
- **THEN** it returns `undefined` (no code replacement, Shiki processes the original)

### Requirement: Root hook injects hovers from matched result
The transformer's `root` hook SHALL inject hover popup elements into the HAST tree using the result matched during `preprocess`.

#### Scenario: Hovers injected
- **WHEN** the matched result contains hover data at line 0, character 4
- **THEN** the HAST tree contains a `<span class="glosharp-hover">` wrapping the token with CSS anchor positioning and a sibling `<div class="glosharp-popup">` with formatted display parts

### Requirement: Root hook injects errors from matched result
The transformer's `root` hook SHALL inject error annotation elements for unexpected compiler diagnostics from the matched result.

#### Scenario: Errors injected
- **WHEN** the matched result contains an unexpected error
- **THEN** the HAST tree contains an error message element with the diagnostic code and text

#### Scenario: Expected errors skipped
- **WHEN** the matched result contains an error with `expected: true`
- **THEN** no error annotation is injected for that error

### Requirement: Root hook injects completions from matched result
The transformer's `root` hook SHALL inject completion list elements from the matched result.

#### Scenario: Completions injected
- **WHEN** the matched result contains completion data
- **THEN** the HAST tree contains a `<ul class="glosharp-completion-list">` with completion items

### Requirement: Root hook is no-op on miss
The transformer's `root` hook SHALL do nothing if no result was matched during `preprocess`.

#### Scenario: No result matched
- **WHEN** `preprocess` did not find a matching result in the map
- **THEN** `root` does not modify the HAST tree

### Requirement: Transformer works across multiple codeToHtml calls
A single transformer instance returned by `transformerGloSharpFromMap` SHALL correctly handle multiple sequential `codeToHtml` calls, matching each to the correct result in the map.

#### Scenario: Multiple renders with one transformer
- **WHEN** `codeToHtml` is called 3 times with different code blocks, all using the same transformer instance
- **THEN** each call gets the correct hovers/errors/completions for its specific code block

### Requirement: Transformer has name property
The transformer SHALL have `name: 'glosharp'` for identification in Shiki's transformer pipeline.

#### Scenario: Transformer name
- **WHEN** the transformer is inspected
- **THEN** `transformer.name` equals `'glosharp'`
