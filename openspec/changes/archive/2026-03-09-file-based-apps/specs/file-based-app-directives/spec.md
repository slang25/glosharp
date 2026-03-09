## ADDED Requirements

### Requirement: Parse file-based app directives
The system SHALL recognize lines starting with `#:` as file-based app directives. Supported directive types SHALL include `#:package`, `#:sdk`, `#:property`, and `#:project`.

#### Scenario: Parse package directive with version
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3`
- **THEN** the system extracts a package directive with name `Newtonsoft.Json` and version `13.0.3`

#### Scenario: Parse package directive with wildcard version
- **WHEN** source contains `#:package Spectre.Console@*`
- **THEN** the system extracts a package directive with name `Spectre.Console` and version `*`

#### Scenario: Parse package directive without version
- **WHEN** source contains `#:package Newtonsoft.Json`
- **THEN** the system extracts a package directive with name `Newtonsoft.Json` and no version constraint

#### Scenario: Parse SDK directive
- **WHEN** source contains `#:sdk Microsoft.NET.Sdk.Web`
- **THEN** the system extracts an SDK directive with value `Microsoft.NET.Sdk.Web`

#### Scenario: Parse property directive
- **WHEN** source contains `#:property TargetFramework=net10.0`
- **THEN** the system extracts a property directive with key `TargetFramework` and value `net10.0`

#### Scenario: Parse project directive
- **WHEN** source contains `#:project ../SharedLib/SharedLib.csproj`
- **THEN** the system extracts a project directive with path `../SharedLib/SharedLib.csproj`

#### Scenario: Multiple directives
- **WHEN** source contains multiple `#:` directive lines
- **THEN** the system extracts all directives in order

### Requirement: Strip directives from output
All `#:` directive lines SHALL be removed from the processed output code. The `original` field SHALL preserve the `#:` lines.

#### Scenario: Directives removed from code
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3` followed by `using Newtonsoft.Json;` and code
- **THEN** the `code` output does not contain the `#:package` line, but `original` does

#### Scenario: Line positions adjusted after stripping
- **WHEN** source has 2 `#:` directive lines followed by code starting at source line 3
- **THEN** the code in processed output starts at line 0 and all hover/error positions are adjusted accordingly

### Requirement: Strip directives from compilation code
`#:` directive lines SHALL be removed from the code passed to Roslyn for compilation, since Roslyn does not understand `#:` syntax.

#### Scenario: Roslyn does not see directives
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3` followed by valid C# code
- **THEN** the compilation code passed to Roslyn does not contain the `#:` line and Roslyn produces no parse errors from directive syntax

### Requirement: Resolve packages via SDK
When `#:package` directives are present and no `--project` flag is provided, the system SHALL delegate package resolution to the .NET SDK by invoking `dotnet build` or `dotnet restore` on the source file, then reading the generated `project.assets.json`.

#### Scenario: Package resolved via SDK
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3` and no `--project` flag is provided
- **THEN** the system invokes the .NET SDK to resolve the package, reads the generated assets file, and creates `MetadataReference` entries for `Newtonsoft.Json.dll` and its dependencies

#### Scenario: Multiple packages resolved
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3` and `#:package Serilog@3.1.1`
- **THEN** both packages and their transitive dependencies are resolved and available as references

#### Scenario: Resolution failure
- **WHEN** source contains `#:package NonExistent.Package@1.0.0` and SDK resolution fails
- **THEN** the system reports an error to stderr and exits with non-zero code

### Requirement: Require .NET 10+ SDK for file-based app directives
When `#:` directives are detected, the system SHALL verify that the installed .NET SDK is version 10.0 or later. If an older SDK is installed, the system SHALL fail with a clear error message.

#### Scenario: SDK version check passes
- **WHEN** source contains `#:` directives and `dotnet --version` reports 10.0.100 or later
- **THEN** processing proceeds with file-based app resolution

#### Scenario: SDK version check fails
- **WHEN** source contains `#:` directives and `dotnet --version` reports 9.0.100
- **THEN** the system fails with an error message indicating .NET 10 SDK is required and suggesting `--project` as an alternative

### Requirement: SDK-based framework pack resolution
When `#:sdk` directives specify a non-default SDK (e.g., `Microsoft.NET.Sdk.Web`), the SDK's restore SHALL include the additional framework references (e.g., `Microsoft.AspNetCore.App.Ref`). The system SHALL read these from the generated `project.assets.json` without custom logic.

#### Scenario: Web SDK includes ASP.NET references
- **WHEN** source contains `#:sdk Microsoft.NET.Sdk.Web` and `#:package` directives
- **THEN** the resolved references include ASP.NET framework assemblies in addition to the base framework

#### Scenario: Default SDK uses base framework only
- **WHEN** source contains `#:package` directives but no `#:sdk` directive
- **THEN** the resolved references include only the base `Microsoft.NETCore.App` framework assemblies

### Requirement: Populate meta from directives
The `meta` object in JSON output SHALL include `packages` populated from `#:package` directives and `sdk` populated from `#:sdk` directive (or null if not specified).

#### Scenario: Meta packages from directives
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3`
- **THEN** `meta.packages` contains `[{ "name": "Newtonsoft.Json", "version": "13.0.3" }]`

#### Scenario: Meta SDK from directive
- **WHEN** source contains `#:sdk Microsoft.NET.Sdk.Web`
- **THEN** `meta.sdk` is `"Microsoft.NET.Sdk.Web"`

#### Scenario: Meta SDK absent when not specified
- **WHEN** source contains no `#:sdk` directive
- **THEN** `meta.sdk` is null or omitted
