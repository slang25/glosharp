## ADDED Requirements

### Requirement: Config file option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `configFile` property specifying an explicit path to a `glosharp.config.json` file.

#### Scenario: Explicit config file passed to CLI
- **WHEN** `createGloSharp({ configFile: './glosharp.config.json' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--config ./glosharp.config.json`

#### Scenario: No config file specified
- **WHEN** `createGloSharp()` is called without `configFile` and `process()` is invoked
- **THEN** the CLI is spawned without `--config` (auto-discovery handled by CLI)

### Requirement: Config file option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `configFile` property that overrides the instance-level `configFile` for a single call.

#### Scenario: Per-call config override
- **WHEN** `glosharp.process({ code: '...', configFile: './other.json' })` is called on an instance with a different `configFile`
- **THEN** the CLI is spawned with `--config ./other.json`

#### Scenario: Per-call config without instance default
- **WHEN** `glosharp.process({ code: '...', configFile: './custom.json' })` is called on an instance without `configFile`
- **THEN** the CLI is spawned with `--config ./custom.json`
