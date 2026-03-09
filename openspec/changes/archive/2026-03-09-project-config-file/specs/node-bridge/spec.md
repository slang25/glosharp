## ADDED Requirements

### Requirement: Config file option in TwohashOptions
The `TwohashOptions` interface SHALL accept an optional `configFile` property specifying an explicit path to a `twohash.config.json` file.

#### Scenario: Explicit config file passed to CLI
- **WHEN** `createTwohash({ configFile: './twohash.config.json' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--config ./twohash.config.json`

#### Scenario: No config file specified
- **WHEN** `createTwohash()` is called without `configFile` and `process()` is invoked
- **THEN** the CLI is spawned without `--config` (auto-discovery handled by CLI)

### Requirement: Config file option in TwohashProcessOptions
The `TwohashProcessOptions` interface SHALL accept an optional `configFile` property that overrides the instance-level `configFile` for a single call.

#### Scenario: Per-call config override
- **WHEN** `twohash.process({ code: '...', configFile: './other.json' })` is called on an instance with a different `configFile`
- **THEN** the CLI is spawned with `--config ./other.json`

#### Scenario: Per-call config without instance default
- **WHEN** `twohash.process({ code: '...', configFile: './custom.json' })` is called on an instance without `configFile`
- **THEN** the CLI is spawned with `--config ./custom.json`
