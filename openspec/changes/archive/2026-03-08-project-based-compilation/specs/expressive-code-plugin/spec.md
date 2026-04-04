## ADDED Requirements

### Requirement: Pass project option to bridge
The `pluginGloSharp()` factory SHALL accept a `project` option and pass it through to the glosharp bridge when processing code blocks.

#### Scenario: Plugin with project context
- **WHEN** `pluginGloSharp({ project: './MyProject.csproj' })` is configured
- **THEN** all glosharp CLI invocations include the `--project` argument

#### Scenario: Plugin without project
- **WHEN** `pluginGloSharp()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)
