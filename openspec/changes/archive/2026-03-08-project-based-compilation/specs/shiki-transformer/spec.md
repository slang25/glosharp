## ADDED Requirements

### Requirement: Pass project option to bridge
The `transformerTwohash()` factory SHALL accept a `project` option and pass it through to the twohash bridge when processing code blocks.

#### Scenario: Transformer with project context
- **WHEN** `transformerTwohash({ project: './MyProject.csproj' })` is configured
- **THEN** all twohash CLI invocations include the `--project` argument

#### Scenario: Transformer without project
- **WHEN** `transformerTwohash()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)
