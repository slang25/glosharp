## ADDED Requirements

### Requirement: Region option in process call
The `process()` method SHALL accept a `region` option in `GloSharpProcessOptions` and pass it as `--region <name>` to the CLI.

#### Scenario: Process with region
- **WHEN** `glosharp.process({ file: 'src/Example.cs', region: 'getting-started' })` is called
- **THEN** the CLI is spawned with `--region getting-started` argument

#### Scenario: Process without region
- **WHEN** `glosharp.process({ file: 'src/Example.cs' })` is called without a `region` option
- **THEN** the CLI is spawned without the `--region` argument

### Requirement: TypeScript types include completion structures
The package SHALL export `GloSharpCompletion` and `GloSharpCompletionItem` interfaces. `GloSharpResult.completions` SHALL be typed as `GloSharpCompletion[]` instead of `object[]`.

#### Scenario: Type-safe completion access
- **WHEN** a consumer accesses `result.completions[0].items[0].label`
- **THEN** TypeScript provides autocompletion and type checking for completion fields
