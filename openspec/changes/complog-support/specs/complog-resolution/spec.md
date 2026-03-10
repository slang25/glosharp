## ADDED Requirements

### Requirement: Open complog and extract compilation references
The system SHALL accept a complog file path and return resolved `MetadataReference[]`, `CSharpCompilationOptions`, `CSharpParseOptions`, target framework string, and package list by opening the complog with `Basic.CompilerLog.Util` and extracting data from the recreated `Compilation`.

#### Scenario: Single-project complog
- **WHEN** `ComplogResolver` is given a `.complog` file containing one C# compilation
- **THEN** it returns the references, compilation options, parse options, target framework, and package list from that compilation

#### Scenario: Complog file not found
- **WHEN** `ComplogResolver` is given a path to a non-existent file
- **THEN** it throws a `FileNotFoundException` with a message indicating the complog file was not found

#### Scenario: Invalid complog file
- **WHEN** `ComplogResolver` is given a path to a file that is not a valid complog
- **THEN** it throws an exception with a descriptive error message

### Requirement: Select compilation by project name
The system SHALL accept an optional project name to select a specific compilation from a multi-project complog. When not specified, the first C# compilation SHALL be used.

#### Scenario: Multi-project complog with project name
- **WHEN** a complog contains compilations for `MyApp` and `MyLib`, and `complogProject: "MyLib"` is specified
- **THEN** references and options are extracted from the `MyLib` compilation

#### Scenario: Multi-project complog without project name
- **WHEN** a complog contains compilations for `MyApp` and `MyLib`, and no project name is specified
- **THEN** references and options are extracted from the first C# compilation

#### Scenario: Project name not found in complog
- **WHEN** a complog contains compilations for `MyApp` and `MyLib`, and `complogProject: "NonExistent"` is specified
- **THEN** an error is thrown listing the available project names

#### Scenario: Complog with no C# compilations
- **WHEN** a complog contains only F# or VB compilations
- **THEN** an error is thrown indicating no C# compilations were found

### Requirement: ComplogResolver is disposable
`ComplogResolver` SHALL implement `IDisposable` to release file handles held by the complog reader.

#### Scenario: Dispose releases resources
- **WHEN** `ComplogResolver` is disposed
- **THEN** the underlying complog reader is disposed and file handles are released

### Requirement: Extract package information from complog
The system SHALL extract NuGet package references from the complog compilation's metadata references (assembly names and versions) to populate `meta.packages`.

#### Scenario: Complog with NuGet packages
- **WHEN** a complog compilation references `Newtonsoft.Json` version 13.0.3
- **THEN** the resolved package list includes `{name: "Newtonsoft.Json", version: "13.0.3"}`

#### Scenario: Complog with framework-only references
- **WHEN** a complog compilation has only framework references (no NuGet packages)
- **THEN** the resolved package list is empty

### Requirement: Complog resolution bypasses other resolvers
When a complog path is provided to `TwohashProcessor`, the processor SHALL skip `FrameworkResolver`, `ProjectAssetsResolver`, and `FileBasedAppResolver` entirely and use only the complog-sourced references.

#### Scenario: Complog path provided with file-based app directives
- **WHEN** `--complog` is specified and source contains `#:package` directives
- **THEN** directives are parsed and stripped from output, but resolution uses only complog references

#### Scenario: Complog path provided
- **WHEN** `--complog build.complog` is specified
- **THEN** `FrameworkResolver` and `ProjectAssetsResolver` are not invoked

### Requirement: Complog references cached in compilation context cache
Complog-sourced references SHALL be cached in the `CompilationContextCache` keyed by complog file path, project name, and last-write-time of the complog file.

#### Scenario: Second file reuses complog references
- **WHEN** `verify` processes file A then file B, both using the same `--complog`
- **THEN** complog resolution runs only for file A; file B reuses cached references

#### Scenario: Updated complog invalidates cache
- **WHEN** the complog file is regenerated between two process calls within the same process
- **THEN** the cache key differs (due to last-write-time change) and references are re-resolved
