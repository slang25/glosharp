## ADDED Requirements

### Requirement: Export map-based transformer factory
The package SHALL export a `transformerGloSharpFromMap(resultMap, options?)` function that accepts a `GloSharpResultMap` and returns a `ShikiTransformer`. `options` MAY give `keyOptions` (the options the map was keyed with; default `resultMap.keyOptions`), `blockOptions(code, meta)` for per-block overrides, and the render options (`focusable`, `completionLimit`).

#### Scenario: Create transformer from result map
- **WHEN** `transformerGloSharpFromMap(resultMap)` is called with a populated result map
- **THEN** it returns a Shiki transformer object with `preprocess` and `root` hooks

### Requirement: Preprocess looks up result by snippet key
The transformer's `preprocess` hook SHALL compute `snippetKey(code, { ...keyOptions, ...blockOptions(code, meta) })` and look it up in the result map, falling back to a raw SHA-256 of the code for maps built before snippet keys. If found, it SHALL return `result.code` (cleaned code with markers removed).

#### Scenario: Code found in map
- **WHEN** the transformer's `preprocess` receives code that was batch-processed
- **THEN** it returns the cleaned code from the corresponding `GloSharpResult`

#### Scenario: Pipeline stripped the trailing newline
- **WHEN** the map was built from `"…\n"` and Shiki receives the code without the trailing newline (Astro) or with CRLF line endings
- **THEN** the lookup still finds the result

#### Scenario: Code not found in map
- **WHEN** the transformer's `preprocess` receives code that was not batch-processed
- **THEN** it returns `undefined` (no code replacement, Shiki processes the original)

### Requirement: Root hook injects hovers from matched result
The transformer's `root` hook SHALL inject hover popup elements into the HAST tree using the result matched during `preprocess`, as specified by the shiki-transformer capability.

#### Scenario: Hovers injected
- **WHEN** the matched result contains hover data at line 0, character 4
- **THEN** the HAST tree contains a `<span class="glosharp-hover">` wrapping the token with CSS anchor positioning and a child `<span class="glosharp-popup">` with formatted display parts

### Requirement: Root hook injects errors from matched result
The transformer's `root` hook SHALL inject an error underline and an error message for every compiler diagnostic in the matched result. Expected diagnostics (declared with `// @errors:`) SHALL be rendered too — that is how an author shows an error on purpose (twoslash semantics) — and carry the extra class `glosharp-error-expected`.

#### Scenario: Errors injected
- **WHEN** the matched result contains an unexpected error
- **THEN** the HAST tree contains an error message element with the diagnostic code and text

#### Scenario: Expected errors shown
- **WHEN** the matched result contains an error with `expected: true`
- **THEN** the error is rendered, with class `glosharp-error-expected` on its message

### Requirement: Root hook injects completions from matched result
The transformer's `root` hook SHALL inject completion list elements from the matched result.

#### Scenario: Completions injected
- **WHEN** the matched result contains completion data
- **THEN** the HAST tree contains a `glosharp-completion-list` element with completion items

### Requirement: Root hook is no-op on miss
The transformer's `root` hook SHALL do nothing if no result was matched during `preprocess`.

#### Scenario: No result matched
- **WHEN** `preprocess` did not find a matching result in the map
- **THEN** `root` does not modify the HAST tree

### Requirement: Transformer works across multiple codeToHtml calls
A single transformer instance returned by `transformerGloSharpFromMap` SHALL correctly handle multiple sequential or concurrent `codeToHtml` calls, matching each to the correct result in the map. Per-call state SHALL live in Shiki's per-call transformer context (`this.meta`), not in the transformer closure.

#### Scenario: Multiple renders with one transformer
- **WHEN** `codeToHtml` is called 3 times concurrently with different code blocks, all using the same transformer instance
- **THEN** each call gets the correct hovers/errors/completions for its specific code block

### Requirement: Transformer has name property
The transformer SHALL have `name: 'glosharp'` for identification in Shiki's transformer pipeline.

#### Scenario: Transformer name
- **WHEN** the transformer is inspected
- **THEN** `transformer.name` equals `'glosharp'`
