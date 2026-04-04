## ADDED Requirements

### Requirement: Region option for process command
The CLI SHALL accept a `--region` option on the `process` command specifying a `#region` name to extract from the source file.

#### Scenario: Process a specific region
- **WHEN** `glosharp process src/Example.cs --region getting-started` is run
- **THEN** the CLI extracts the named region from the file, compiles the full file, and outputs JSON with only the region's code

#### Scenario: Process without region
- **WHEN** `glosharp process src/Example.cs` is run without `--region`
- **THEN** the CLI processes the entire file as before (no behavior change)

#### Scenario: Region not found exits with error
- **WHEN** `glosharp process src/Example.cs --region nonexistent` is run and the file has no such region
- **THEN** the CLI exits with non-zero code and writes an error to stderr

### Requirement: Region option for verify command
The CLI SHALL accept a `--region` option on the `verify` command to verify only the specified region in each file that contains it.

#### Scenario: Verify with region
- **WHEN** `glosharp verify samples/ --region getting-started` is run
- **THEN** files containing the named region are verified; files without it are skipped

### Requirement: Region option incompatible with stdin
The CLI SHALL reject `--region` when used with `--stdin`, since region extraction requires a file with `#region` directives.

#### Scenario: Region with stdin rejected
- **WHEN** `glosharp process --stdin --region getting-started` is run
- **THEN** the CLI exits with non-zero code and an error message explaining the incompatibility
