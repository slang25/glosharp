## ADDED Requirements

### Requirement: Config file schema
The system SHALL support a `glosharp.config.json` file with the following optional properties: `framework` (string), `project` (string), `cacheDir` (string), `noRestore` (boolean), and `render` (object with optional `theme` and `standalone` properties).

#### Scenario: Full config file
- **WHEN** a `glosharp.config.json` contains `{"framework": "net9.0", "project": "./Samples.csproj", "cacheDir": ".glosharp-cache", "noRestore": true, "render": {"theme": "github-light", "standalone": true}}`
- **THEN** all properties are parsed and available to the CLI commands

#### Scenario: Partial config file
- **WHEN** a `glosharp.config.json` contains only `{"framework": "net9.0"}`
- **THEN** only `framework` is set from config; all other options use their built-in defaults

#### Scenario: Empty config file
- **WHEN** a `glosharp.config.json` contains `{}`
- **THEN** all options use their built-in defaults

### Requirement: Unknown properties ignored
The system SHALL ignore unknown properties in `glosharp.config.json` without producing errors.

#### Scenario: Config with extra properties
- **WHEN** a `glosharp.config.json` contains `{"framework": "net9.0", "futureOption": true}`
- **THEN** `framework` is parsed and `futureOption` is silently ignored

### Requirement: Config file discovery by walking up directories
The system SHALL discover `glosharp.config.json` by starting from the input file's parent directory (or CWD for stdin) and walking up parent directories until a `glosharp.config.json` is found or the filesystem root is reached.

#### Scenario: Config in same directory as input file
- **WHEN** `glosharp process samples/Example.cs` is run and `samples/glosharp.config.json` exists
- **THEN** the config file in `samples/` is loaded

#### Scenario: Config in parent directory
- **WHEN** `glosharp process samples/nested/Example.cs` is run and `glosharp.config.json` exists only at the repo root
- **THEN** the config file at the repo root is loaded

#### Scenario: Config in stdin mode uses CWD
- **WHEN** `glosharp process --stdin` is run from `/project/docs/` and `glosharp.config.json` exists at `/project/`
- **THEN** the config file at `/project/` is loaded

#### Scenario: No config file found
- **WHEN** no `glosharp.config.json` exists in any parent directory
- **THEN** all options use their built-in defaults (no error produced)

### Requirement: CLI arguments override config values
CLI arguments SHALL take precedence over config file values. The effective value for each option SHALL be: CLI argument if provided, otherwise config value if present, otherwise built-in default.

#### Scenario: CLI overrides config framework
- **WHEN** config contains `{"framework": "net9.0"}` and CLI specifies `--framework net8.0`
- **THEN** `net8.0` is used

#### Scenario: Config provides default when CLI omits
- **WHEN** config contains `{"project": "./Samples.csproj"}` and CLI does not specify `--project`
- **THEN** `./Samples.csproj` is used as the project path

#### Scenario: CLI flag overrides config boolean
- **WHEN** config contains `{"noRestore": true}` and CLI does not specify `--no-restore`
- **THEN** `noRestore` is true (from config)

### Requirement: Config project paths resolve relative to config file location
Project paths (`project`, `cacheDir`) in the config file SHALL be resolved relative to the directory containing the `glosharp.config.json` file, not relative to the CWD.

#### Scenario: Relative project path in config
- **WHEN** config at `/repo/glosharp.config.json` contains `{"project": "./samples/Samples.csproj"}`
- **THEN** the project path resolves to `/repo/samples/Samples.csproj`

#### Scenario: Relative cache-dir in config
- **WHEN** config at `/repo/glosharp.config.json` contains `{"cacheDir": ".glosharp-cache"}`
- **THEN** the cache directory resolves to `/repo/.glosharp-cache`

#### Scenario: Absolute paths unchanged
- **WHEN** config contains `{"project": "/absolute/path/Project.csproj"}`
- **THEN** the path is used as-is

### Requirement: Invalid config file produces error
The system SHALL exit with non-zero code and write an error to stderr when a config file exists but contains invalid JSON.

#### Scenario: Malformed JSON
- **WHEN** `glosharp.config.json` contains `{invalid json`
- **THEN** the CLI exits with non-zero code and writes a parse error to stderr

### Requirement: Explicit config path via --config flag
The CLI SHALL accept a `--config <path>` option on all commands (`process`, `verify`, `render`) specifying an explicit path to a config file. When specified, auto-discovery SHALL be skipped.

#### Scenario: Explicit config path
- **WHEN** `glosharp process file.cs --config ./custom/glosharp.config.json` is run
- **THEN** the specified config file is loaded instead of auto-discovery

#### Scenario: Explicit config file not found
- **WHEN** `glosharp process file.cs --config ./nonexistent.json` is run
- **THEN** the CLI exits with non-zero code and writes an error to stderr

#### Scenario: Explicit config disables discovery
- **WHEN** `glosharp process file.cs --config ./custom.json` is run and a `glosharp.config.json` exists in the file's directory
- **THEN** only `./custom.json` is loaded; the discovered config is ignored
