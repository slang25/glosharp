## ADDED Requirements

### Requirement: Complog option on process command
The CLI SHALL accept a `--complog <path>` option on the `process` command specifying a `.complog` file for compilation resolution. Before processing, the CLI SHALL load config file defaults and merge them with CLI arguments, with CLI arguments taking precedence.

#### Scenario: Process with complog
- **WHEN** `glosharp process snippet.cs --complog build.complog` is run
- **THEN** the CLI uses the complog file for reference resolution, bypassing project and framework resolution

#### Scenario: Process with complog from config
- **WHEN** `glosharp process snippet.cs` is run and `glosharp.config.json` contains `{"complog": "./build.complog"}`
- **THEN** the CLI uses the complog path from config

#### Scenario: Complog file not found
- **WHEN** `glosharp process snippet.cs --complog nonexistent.complog` is run
- **THEN** the CLI exits with non-zero code and writes an error to stderr

### Requirement: Complog option on verify command
The CLI SHALL accept a `--complog <path>` option on the `verify` command, applying the same complog context to all files being verified.

#### Scenario: Verify with complog
- **WHEN** `glosharp verify samples/ --complog build.complog` is run
- **THEN** all `.cs` files are compiled using references from the complog

### Requirement: Complog option on render command
The CLI SHALL accept a `--complog <path>` option on the `render` command with the same behavior as on `process`.

#### Scenario: Render with complog
- **WHEN** `glosharp render snippet.cs --complog build.complog` is run
- **THEN** the CLI uses the complog for reference resolution and produces HTML output

### Requirement: Complog-project option on all commands
The CLI SHALL accept a `--complog-project <name>` option on `process`, `verify`, and `render` commands to select a specific project from a multi-project complog.

#### Scenario: Process with complog project selection
- **WHEN** `glosharp process snippet.cs --complog build.complog --complog-project MyLib` is run
- **THEN** the CLI uses the `MyLib` compilation from the complog

#### Scenario: Complog-project without complog
- **WHEN** `glosharp process snippet.cs --complog-project MyLib` is run without `--complog`
- **THEN** the CLI exits with non-zero code and writes an error indicating `--complog-project` requires `--complog`

### Requirement: Complog mutually exclusive with project
The CLI SHALL reject the combination of `--complog` and `--project` options with a clear error message.

#### Scenario: Both complog and project specified
- **WHEN** `glosharp process snippet.cs --complog build.complog --project MyProject.csproj` is run
- **THEN** the CLI exits with non-zero code and writes an error indicating the options are mutually exclusive
