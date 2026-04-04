## ADDED Requirements

### Requirement: Complog option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `complog` property specifying a path to a `.complog` file, applied to all `process()` calls on the instance.

#### Scenario: Instance-level complog
- **WHEN** `createGloSharp({ complog: './build.complog' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--complog ./build.complog`

### Requirement: Complog option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `complog` property that overrides the instance-level `complog` for a single call.

#### Scenario: Per-call complog override
- **WHEN** `glosharp.process({ code: '...', complog: './other.complog' })` is called on an instance with a different `complog`
- **THEN** the CLI is spawned with `--complog ./other.complog`

#### Scenario: No complog
- **WHEN** `glosharp.process({ code: '...' })` is called on an instance without `complog`
- **THEN** the CLI is spawned without `--complog` (existing behavior unchanged)

### Requirement: ComplogProject option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `complogProject` property specifying the project name to select from a multi-project complog.

#### Scenario: Instance-level complog project
- **WHEN** `createGloSharp({ complog: './build.complog', complogProject: 'MyLib' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--complog ./build.complog --complog-project MyLib`

### Requirement: ComplogProject option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `complogProject` property that overrides the instance-level value for a single call.

#### Scenario: Per-call complog project override
- **WHEN** `glosharp.process({ code: '...', complogProject: 'MyApp' })` is called
- **THEN** the CLI is spawned with `--complog-project MyApp`
