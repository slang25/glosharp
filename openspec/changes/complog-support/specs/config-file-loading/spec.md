## MODIFIED Requirements

### Requirement: Config file schema
The system SHALL support a `twohash.config.json` file with the following optional properties: `framework` (string), `project` (string), `cacheDir` (string), `noRestore` (boolean), `complog` (string), `complogProject` (string), and `render` (object with optional `theme` and `standalone` properties).

#### Scenario: Full config file
- **WHEN** a `twohash.config.json` contains `{"framework": "net9.0", "project": "./Samples.csproj", "cacheDir": ".twohash-cache", "noRestore": true, "render": {"theme": "github-light", "standalone": true}}`
- **THEN** all properties are parsed and available to the CLI commands

#### Scenario: Partial config file
- **WHEN** a `twohash.config.json` contains only `{"framework": "net9.0"}`
- **THEN** only `framework` is set from config; all other options use their built-in defaults

#### Scenario: Empty config file
- **WHEN** a `twohash.config.json` contains `{}`
- **THEN** all options use their built-in defaults

#### Scenario: Config with complog
- **WHEN** a `twohash.config.json` contains `{"complog": "./artifacts/build.complog", "complogProject": "MyLib"}`
- **THEN** `complog` and `complogProject` are parsed and available to the CLI commands

## ADDED Requirements

### Requirement: Config complog path resolves relative to config file
The `complog` path in the config file SHALL be resolved relative to the directory containing the `twohash.config.json` file, consistent with how `project` and `cacheDir` paths are resolved.

#### Scenario: Relative complog path in config
- **WHEN** config at `/repo/twohash.config.json` contains `{"complog": "./artifacts/build.complog"}`
- **THEN** the complog path resolves to `/repo/artifacts/build.complog`

#### Scenario: Absolute complog path in config
- **WHEN** config contains `{"complog": "/absolute/path/build.complog"}`
- **THEN** the path is used as-is
