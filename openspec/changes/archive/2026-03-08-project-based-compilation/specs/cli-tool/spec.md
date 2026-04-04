## ADDED Requirements

### Requirement: Project option for process command
The CLI SHALL accept a `--project` option on the `process` command specifying a .csproj file or directory. When provided, the CLI resolves NuGet package references from the project's `project.assets.json`.

#### Scenario: Process with project context
- **WHEN** `glosharp process snippet.cs --project ./MyProject.csproj` is run
- **THEN** the CLI resolves NuGet packages from `MyProject`'s `obj/project.assets.json` and compiles with those references

#### Scenario: Process with project directory
- **WHEN** `glosharp process snippet.cs --project ./MyProject/` is run
- **THEN** the CLI locates the .csproj in that directory and resolves packages

### Requirement: Project option for verify command
The CLI SHALL accept a `--project` option on the `verify` command, applying the same project context to all files being verified.

#### Scenario: Verify with project context
- **WHEN** `glosharp verify samples/ --project ./Samples.csproj` is run
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
