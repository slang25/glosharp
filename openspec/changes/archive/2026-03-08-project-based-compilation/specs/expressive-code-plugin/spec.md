## ADDED Requirements

### Requirement: Pass project option to bridge
The `pluginTwohash()` factory SHALL accept a `project` option and pass it through to the twohash bridge when processing code blocks.

#### Scenario: Plugin with project context
- **WHEN** `pluginTwohash({ project: './MyProject.csproj' })` is configured
- **THEN** all twohash CLI invocations include the `--project` argument

#### Scenario: Plugin without project
- **WHEN** `pluginTwohash()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)
