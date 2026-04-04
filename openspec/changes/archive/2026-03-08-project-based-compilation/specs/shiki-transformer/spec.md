## ADDED Requirements

### Requirement: Pass project option to bridge
The `transformerGloSharp()` factory SHALL accept a `project` option and pass it through to the glosharp bridge when processing code blocks.

#### Scenario: Transformer with project context
- **WHEN** `transformerGloSharp({ project: './MyProject.csproj' })` is configured
- **THEN** all glosharp CLI invocations include the `--project` argument

#### Scenario: Transformer without project
- **WHEN** `transformerGloSharp()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)
