## ADDED Requirements

### Requirement: Process command accepts file path
The CLI SHALL accept a `process` command with a file path argument to process a C# source file and output JSON to stdout.

#### Scenario: Process a file
- **WHEN** `twohash process src/Example.cs` is run
- **THEN** the CLI reads the file, processes it through the core, and writes JSON to stdout

### Requirement: Process command accepts stdin
The CLI SHALL accept source code from stdin when no file path is provided or when `--stdin` is specified.

#### Scenario: Pipe source via stdin
- **WHEN** `echo "var x = 42;" | twohash process --stdin` is run
- **THEN** the CLI reads from stdin, processes it, and writes JSON to stdout

### Requirement: Target framework option
The CLI SHALL accept a `--framework` option to specify the target framework moniker (e.g., `net8.0`, `net9.0`).

#### Scenario: Specify target framework
- **WHEN** `twohash process file.cs --framework net8.0` is run
- **THEN** the core uses `net8.0` framework reference assemblies for compilation

### Requirement: JSON output to stdout
The CLI SHALL write only valid JSON to stdout. Diagnostic messages, progress, and errors SHALL go to stderr.

#### Scenario: Clean stdout
- **WHEN** the CLI processes a file successfully
- **THEN** stdout contains only the JSON output, and any warnings go to stderr

### Requirement: Exit code reflects compilation status
The CLI SHALL exit with code 0 when processing succeeds (all errors are expected or `@noErrors` passes). The CLI SHALL exit with non-zero code when unexpected compilation errors occur.

#### Scenario: Exit 0 on success
- **WHEN** source compiles cleanly or all errors are declared via `// @errors:`
- **THEN** the CLI exits with code 0

#### Scenario: Non-zero exit on unexpected error
- **WHEN** source has compilation errors not declared via `// @errors:`
- **THEN** the CLI exits with non-zero code and writes error details to stderr

### Requirement: Installable as dotnet tool
The CLI SHALL be packable and installable as a .NET global or local tool via `dotnet tool install`.

#### Scenario: Global tool install
- **WHEN** `dotnet tool install -g twohash` is run
- **THEN** the `twohash` command becomes available on the PATH

### Requirement: Verify command for CI
The CLI SHALL accept a `verify` command that processes all `.cs` files in a directory and exits non-zero if any have unexpected compilation errors.

#### Scenario: Verify a samples directory
- **WHEN** `twohash verify samples/` is run and all files compile
- **THEN** the CLI exits with code 0

#### Scenario: Verify fails on error
- **WHEN** `twohash verify samples/` is run and one file has unexpected errors
- **THEN** the CLI exits with non-zero code and reports which file(s) failed to stderr

### Requirement: Project option for process command
The CLI SHALL accept a `--project` option on the `process` command specifying a .csproj file or directory. When provided, the CLI resolves NuGet package references from the project's `project.assets.json`.

#### Scenario: Process with project context
- **WHEN** `twohash process snippet.cs --project ./MyProject.csproj` is run
- **THEN** the CLI resolves NuGet packages from `MyProject`'s `obj/project.assets.json` and compiles with those references

#### Scenario: Process with project directory
- **WHEN** `twohash process snippet.cs --project ./MyProject/` is run
- **THEN** the CLI locates the .csproj in that directory and resolves packages

### Requirement: Project option for verify command
The CLI SHALL accept a `--project` option on the `verify` command, applying the same project context to all files being verified.

#### Scenario: Verify with project context
- **WHEN** `twohash verify samples/ --project ./Samples.csproj` is run
- **THEN** all `.cs` files are compiled with NuGet packages from the project

### Requirement: Auto-restore when assets missing
The CLI SHALL run `dotnet restore` on the project when `project.assets.json` is not found, unless `--no-restore` is specified.

#### Scenario: Auto-restore triggered
- **WHEN** `--project ./MyProject.csproj` is specified and `obj/project.assets.json` does not exist
- **THEN** the CLI runs `dotnet restore` on the project, then proceeds with resolution

#### Scenario: Auto-restore skipped
- **WHEN** `--project ./MyProject.csproj` and `--no-restore` are specified and `obj/project.assets.json` does not exist
- **THEN** the CLI fails with an error indicating the assets file is missing

#### Scenario: Restore failure
- **WHEN** auto-restore is triggered but `dotnet restore` fails
- **THEN** the CLI exits with non-zero code and writes the restore error to stderr

### Requirement: No-restore flag
The CLI SHALL accept a `--no-restore` flag that prevents automatic `dotnet restore` execution.

#### Scenario: No-restore flag accepted
- **WHEN** `--no-restore` is specified
- **THEN** the CLI does not invoke `dotnet restore` regardless of assets file state
