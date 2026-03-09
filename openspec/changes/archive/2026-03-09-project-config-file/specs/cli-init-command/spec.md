## ADDED Requirements

### Requirement: Init command creates config file
The CLI SHALL accept an `init` command that creates a `twohash.config.json` file in the current directory with all properties set to their default values.

#### Scenario: Init creates config file
- **WHEN** `twohash init` is run in a directory without `twohash.config.json`
- **THEN** a `twohash.config.json` file is created with default values and the CLI exits with code 0

#### Scenario: Init prints explanation
- **WHEN** `twohash init` is run
- **THEN** a human-readable message explaining the config properties is written to stderr

### Requirement: Init refuses to overwrite existing config
The CLI SHALL refuse to overwrite an existing `twohash.config.json` unless `--force` is specified.

#### Scenario: Config already exists
- **WHEN** `twohash init` is run in a directory that already contains `twohash.config.json`
- **THEN** the CLI exits with non-zero code and writes an error to stderr indicating the file already exists

#### Scenario: Force overwrite
- **WHEN** `twohash init --force` is run in a directory that already contains `twohash.config.json`
- **THEN** the existing file is overwritten with fresh defaults

### Requirement: Init default content
The `init` command SHALL produce a valid JSON file containing: `{"framework": "net9.0", "project": null, "cacheDir": null, "noRestore": false, "render": {"theme": "github-dark", "standalone": false}}`.

#### Scenario: Default file content is valid JSON
- **WHEN** `twohash init` creates a config file
- **THEN** the file is parseable as valid JSON and contains all config properties
