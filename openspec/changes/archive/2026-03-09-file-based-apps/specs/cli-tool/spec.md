## ADDED Requirements

### Requirement: Auto-detect file-based app mode
The CLI SHALL auto-detect file-based app mode when no `--project` flag is provided and the source file contains `#:` directive lines. In this mode, the CLI SHALL use SDK-based resolution instead of framework-only mode.

#### Scenario: Auto-detection with directives present
- **WHEN** `glosharp process snippet.cs` is run without `--project` and `snippet.cs` contains `#:package Newtonsoft.Json@13.0.3`
- **THEN** the CLI uses file-based app resolution via the .NET SDK

#### Scenario: No auto-detection without directives
- **WHEN** `glosharp process snippet.cs` is run without `--project` and `snippet.cs` contains no `#:` lines
- **THEN** the CLI uses framework-only resolution (existing behavior unchanged)

#### Scenario: Project flag overrides auto-detection
- **WHEN** `glosharp process snippet.cs --project ./MyProject.csproj` is run and `snippet.cs` contains `#:` directives
- **THEN** the CLI uses project-based resolution from the `.csproj`, ignoring `#:` directives for resolution purposes (directives are still stripped from output)

### Requirement: File-based app mode with verify command
The `verify` command SHALL support file-based app auto-detection per file. Each file SHALL be independently checked for `#:` directives.

#### Scenario: Verify with mixed files
- **WHEN** `glosharp verify samples/` is run and some files contain `#:` directives while others do not
- **THEN** files with `#:` directives use SDK-based resolution; files without use framework-only resolution

### Requirement: No-restore flag applies to file-based app resolution
The `--no-restore` flag SHALL prevent the CLI from invoking `dotnet build` or `dotnet restore` for file-based app resolution. When specified with `#:` directives, the CLI SHALL fail if the generated assets file does not already exist.

#### Scenario: No-restore with file-based app
- **WHEN** `--no-restore` is specified and source contains `#:` directives but no cached assets exist
- **THEN** the CLI fails with an error indicating that restore is required
