## ADDED Requirements

### Requirement: Parse project.assets.json for resolved assemblies
The system SHALL parse a `project.assets.json` file and extract resolved NuGet package assembly paths for a given target framework moniker.

#### Scenario: Resolve packages for net8.0
- **WHEN** `project.assets.json` contains a `targets` entry for `.NETCoreApp,Version=v8.0` with package `Newtonsoft.Json/13.0.3` having a `compile` entry of `lib/net6.0/Newtonsoft.Json.dll`
- **THEN** the resolver returns a `MetadataReference` for `{packageFolder}/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll`

#### Scenario: Multiple packages with transitive dependencies
- **WHEN** `project.assets.json` contains direct and transitive package entries
- **THEN** the resolver returns `MetadataReference` entries for all packages that have compile-time assemblies

#### Scenario: Package with no compile assets
- **WHEN** a package entry has no `compile` dictionary or only contains `_._` placeholder entries
- **THEN** the resolver skips that package without error

### Requirement: Resolve NuGet global packages folder
The system SHALL read the `packageFolders` section of `project.assets.json` to determine the NuGet global packages directory, and resolve assembly paths relative to it.

#### Scenario: Default packages folder
- **WHEN** `packageFolders` contains `~/.nuget/packages/`
- **THEN** assembly paths are resolved relative to that directory

#### Scenario: Custom packages folder
- **WHEN** `packageFolders` contains a non-default path like `/opt/nuget/cache/`
- **THEN** assembly paths are resolved relative to the custom path

### Requirement: Select target framework from assets
The system SHALL select the appropriate target framework from the `targets` section. If a specific TFM is requested, it SHALL match against available targets. If no TFM is requested, it SHALL use the first available target.

#### Scenario: Explicit TFM match
- **WHEN** `net8.0` is requested and targets contains `.NETCoreApp,Version=v8.0`
- **THEN** that target's packages are resolved

#### Scenario: TFM not found
- **WHEN** `net9.0` is requested but targets only contains `.NETCoreApp,Version=v8.0`
- **THEN** the system fails with a clear error listing available target frameworks

#### Scenario: Default TFM selection
- **WHEN** no TFM is specified
- **THEN** the system uses the first target framework listed in the assets file

### Requirement: Extract package metadata
The system SHALL extract package names and versions from the resolved dependencies and return them as structured metadata for populating `TwohashMeta.Packages`.

#### Scenario: Package metadata extraction
- **WHEN** resolved packages include `Newtonsoft.Json/13.0.3` and `System.Text.Json/8.0.0`
- **THEN** the returned metadata includes entries `{ name: "Newtonsoft.Json", version: "13.0.3" }` and `{ name: "System.Text.Json", version: "8.0.0" }`

### Requirement: Locate project.assets.json from project path
The system SHALL accept either a `.csproj` file path or a directory path and locate `obj/project.assets.json` relative to the project directory.

#### Scenario: Path is a .csproj file
- **WHEN** project path is `/path/to/MyProject.csproj`
- **THEN** the system looks for `/path/to/obj/project.assets.json`

#### Scenario: Path is a directory
- **WHEN** project path is `/path/to/MyProject/`
- **THEN** the system looks for `/path/to/MyProject/obj/project.assets.json`

#### Scenario: Assets file not found
- **WHEN** `obj/project.assets.json` does not exist at the resolved path
- **THEN** the system fails with an error indicating the assets file was not found and suggesting `dotnet restore`
