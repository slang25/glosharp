## ADDED Requirements

### Requirement: Config flag on all commands
The CLI SHALL accept a `--config <path>` option on the `process`, `verify`, and `render` commands specifying an explicit path to a config file.

#### Scenario: Config flag on process
- **WHEN** `glosharp process file.cs --config ./glosharp.config.json` is run
- **THEN** the specified config file is loaded and its values are used as defaults

#### Scenario: Config flag on verify
- **WHEN** `glosharp verify samples/ --config ./glosharp.config.json` is run
- **THEN** the specified config file is loaded and its values are used as defaults

#### Scenario: Config flag on render
- **WHEN** `glosharp render file.cs --config ./glosharp.config.json` is run
- **THEN** the specified config file is loaded and its values are used as defaults

### Requirement: Init subcommand
The CLI SHALL accept an `init` subcommand that delegates to the init command logic.

#### Scenario: Init recognized as command
- **WHEN** `glosharp init` is run
- **THEN** the CLI executes the init command (not treated as a file path or unknown command)

## MODIFIED Requirements

### Requirement: Process command accepts file path
The CLI SHALL accept a `process` command with a file path argument to process a C# source file and output JSON to stdout. Before processing, the CLI SHALL load config file defaults (via auto-discovery or `--config`) and merge them with CLI arguments, with CLI arguments taking precedence.

#### Scenario: Process a file
- **WHEN** `glosharp process src/Example.cs` is run
- **THEN** the CLI auto-discovers `glosharp.config.json`, merges config defaults with CLI args, reads the file, processes it through the core, and writes JSON to stdout

#### Scenario: Process with config defaults
- **WHEN** `glosharp process src/Example.cs` is run and `glosharp.config.json` contains `{"framework": "net9.0", "project": "./Samples.csproj"}`
- **THEN** the CLI uses `net9.0` as framework and `./Samples.csproj` as project without explicit CLI flags

### Requirement: Verify command for CI
The CLI SHALL accept a `verify` command that processes all `.cs` files in a directory and exits non-zero if any have unexpected compilation errors. Before processing, the CLI SHALL load config file defaults and merge them with CLI arguments.

#### Scenario: Verify a samples directory
- **WHEN** `glosharp verify samples/` is run and all files compile
- **THEN** the CLI auto-discovers config, merges defaults, and exits with code 0

#### Scenario: Verify fails on error
- **WHEN** `glosharp verify samples/` is run and one file has unexpected errors
- **THEN** the CLI exits with non-zero code and reports which file(s) failed to stderr

#### Scenario: Verify with config defaults
- **WHEN** `glosharp verify samples/` is run and `glosharp.config.json` contains `{"framework": "net9.0", "cacheDir": ".glosharp-cache"}`
- **THEN** the CLI uses `net9.0` and `.glosharp-cache` from config for all files

### Requirement: Render command produces HTML output
The CLI SHALL accept a `render` command with the same file/stdin input and options as `process` (`--framework`, `--project`, `--region`, `--no-restore`, `--cache-dir`, `--config`), plus additional rendering options. Before processing, the CLI SHALL load config file defaults (including `render.theme` and `render.standalone`) and merge them with CLI arguments.

#### Scenario: Render a file to stdout
- **WHEN** `glosharp render src/Example.cs` is run
- **THEN** the CLI auto-discovers config, merges defaults, processes the file, classifies tokens, and writes self-contained HTML to stdout

#### Scenario: Render with config theme
- **WHEN** `glosharp render file.cs` is run and `glosharp.config.json` contains `{"render": {"theme": "github-light"}}`
- **THEN** the HTML uses github-light color scheme from config

#### Scenario: Render CLI theme overrides config
- **WHEN** `glosharp render file.cs --theme github-dark` is run and config contains `{"render": {"theme": "github-light"}}`
- **THEN** the HTML uses github-dark (CLI wins)
