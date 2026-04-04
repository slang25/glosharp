## ADDED Requirements

### Requirement: Project option in process call
The `process()` method SHALL accept a `project` option in `GloSharpProcessOptions` and pass it as `--project <path>` to the CLI.

#### Scenario: Process with project
- **WHEN** `glosharp.process({ code: '...', project: './MyProject.csproj' })` is called
- **THEN** the CLI is spawned with `--project ./MyProject.csproj` argument

#### Scenario: Process without project
- **WHEN** `glosharp.process({ code: '...' })` is called without a `project` option
- **THEN** the CLI is spawned without the `--project` argument (standalone mode)

### Requirement: No-restore option in process call
The `process()` method SHALL accept a `noRestore` boolean option and pass `--no-restore` to the CLI when true.

#### Scenario: No-restore passed to CLI
- **WHEN** `glosharp.process({ code: '...', project: './MyProject.csproj', noRestore: true })` is called
- **THEN** the CLI is spawned with both `--project` and `--no-restore` arguments
