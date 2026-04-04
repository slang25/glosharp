## ADDED Requirements

### Requirement: Process command accepts file path
The CLI SHALL accept a `process` command with a file path argument to process a C# source file and output JSON to stdout.

#### Scenario: Process a file
- **WHEN** `glosharp process src/Example.cs` is run
- **THEN** the CLI reads the file, processes it through the core, and writes JSON to stdout

### Requirement: Process command accepts stdin
The CLI SHALL accept source code from stdin when no file path is provided or when `--stdin` is specified.

#### Scenario: Pipe source via stdin
- **WHEN** `echo "var x = 42;" | glosharp process --stdin` is run
- **THEN** the CLI reads from stdin, processes it, and writes JSON to stdout

### Requirement: Target framework option
The CLI SHALL accept a `--framework` option to specify the target framework moniker (e.g., `net8.0`, `net9.0`).

#### Scenario: Specify target framework
- **WHEN** `glosharp process file.cs --framework net8.0` is run
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
- **WHEN** `dotnet tool install -g glosharp` is run
- **THEN** the `glosharp` command becomes available on the PATH

### Requirement: Verify command for CI
The CLI SHALL accept a `verify` command that processes all `.cs` files in a directory and exits non-zero if any have unexpected compilation errors.

#### Scenario: Verify a samples directory
- **WHEN** `glosharp verify samples/` is run and all files compile
- **THEN** the CLI exits with code 0

#### Scenario: Verify fails on error
- **WHEN** `glosharp verify samples/` is run and one file has unexpected errors
- **THEN** the CLI exits with non-zero code and reports which file(s) failed to stderr
