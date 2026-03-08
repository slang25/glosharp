## ADDED Requirements

### Requirement: Region option in process call
The `process()` method SHALL accept a `region` option in `TwohashProcessOptions` and pass it as `--region <name>` to the CLI.

#### Scenario: Process with region
- **WHEN** `twohash.process({ file: 'src/Example.cs', region: 'getting-started' })` is called
- **THEN** the CLI is spawned with `--region getting-started` argument

#### Scenario: Process without region
- **WHEN** `twohash.process({ file: 'src/Example.cs' })` is called without a `region` option
- **THEN** the CLI is spawned without the `--region` argument

### Requirement: TypeScript types include completion structures
The package SHALL export `TwohashCompletion` and `TwohashCompletionItem` interfaces. `TwohashResult.completions` SHALL be typed as `TwohashCompletion[]` instead of `object[]`.

#### Scenario: Type-safe completion access
- **WHEN** a consumer accesses `result.completions[0].items[0].label`
- **THEN** TypeScript provides autocompletion and type checking for completion fields
