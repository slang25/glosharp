## MODIFIED Requirements

### Requirement: Config file schema
The system SHALL support a `glosharp.config.json` file with the following optional properties: `framework` (string), `project` (string), `cacheDir` (string), `noRestore` (boolean), `complog` (string), `complogProject` (string), `implicitUsings` (string array), `langVersion` (string), `nullable` (string), and `render` (object with optional `theme` and `standalone` properties).

#### Scenario: Full config file
- **WHEN** a `glosharp.config.json` contains `{"framework": "net9.0", "project": "./Samples.csproj", "cacheDir": ".glosharp-cache", "noRestore": true, "implicitUsings": ["System", "System.Linq"], "langVersion": "13", "nullable": "enable", "render": {"theme": "github-light", "standalone": true}}`
- **THEN** all properties are parsed and available to the CLI commands

#### Scenario: Partial config file
- **WHEN** a `glosharp.config.json` contains only `{"framework": "net9.0"}`
- **THEN** only `framework` is set from config; all other options including `implicitUsings`, `langVersion`, and `nullable` use their built-in defaults

#### Scenario: Empty config file
- **WHEN** a `glosharp.config.json` contains `{}`
- **THEN** all options use their built-in defaults
