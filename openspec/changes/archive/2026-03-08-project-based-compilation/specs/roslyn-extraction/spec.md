## MODIFIED Requirements

### Requirement: Resolve framework reference assemblies
The system SHALL locate framework reference assemblies from the installed .NET SDK at `~/.dotnet/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/`. The system SHALL support specifying a target framework moniker. When a project is provided, the system SHALL merge framework references with project package references for compilation.

#### Scenario: Default framework resolution
- **WHEN** no target framework is specified and no project is provided
- **THEN** the system uses the latest installed .NET SDK's reference assemblies

#### Scenario: Specific framework version
- **WHEN** target framework `net8.0` is specified
- **THEN** the system locates and uses `net8.0` reference assemblies

#### Scenario: SDK not found
- **WHEN** no .NET SDK is installed or the specified framework is not available
- **THEN** the system fails with a clear error message indicating the missing SDK

#### Scenario: Project with NuGet packages
- **WHEN** a project path is provided with resolved NuGet packages
- **THEN** the compilation includes both framework reference assemblies and resolved NuGet package assemblies as MetadataReferences

#### Scenario: Project overrides target framework
- **WHEN** a project is provided and no explicit `--framework` is specified
- **THEN** the target framework is inferred from the project's assets file
