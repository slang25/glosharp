## ADDED Requirements

### Requirement: GloContextResolver opens .glocontext files
`GloSharp.Core` SHALL expose a `GloContextResolver` class with a static `Open(string path)` factory and a `Resolve(string? projectName = null)` instance method returning a result with `List<MetadataReference>`, `CSharpCompilationOptions`, `CSharpParseOptions`, and a target framework string. The class SHALL implement `IDisposable`.

#### Scenario: Open and resolve
- **WHEN** `GloContextResolver.Open("ctx.glocontext")` is called on a valid v1 file
- **THEN** a resolver instance is returned that can produce a resolution result via `Resolve()`

#### Scenario: File not found
- **WHEN** `Open` is called with a path that does not exist
- **THEN** it throws `FileNotFoundException` with the offending path

#### Scenario: Dispose releases resources
- **WHEN** a `GloContextResolver` is disposed
- **THEN** subsequent calls to `Resolve` throw `ObjectDisposedException`

### Requirement: Reject unknown format versions
The resolver SHALL read the 6-byte `GLOCTX` magic and 1-byte format version at the start of the file. If the magic does not match, it SHALL throw `InvalidDataException` identifying the file as not a `.glocontext`. If the magic matches but the format version is not supported by this build, it SHALL throw `InvalidDataException` naming the unsupported version.

#### Scenario: Wrong magic bytes
- **WHEN** `Open` is called on a file whose first six bytes are not `GLOCTX`
- **THEN** it throws `InvalidDataException` stating the file is not a `.glocontext`

#### Scenario: Unknown format version
- **WHEN** `Open` is called on a file with `GLOCTX` followed by a format version this build does not understand
- **THEN** it throws `InvalidDataException` naming the version

#### Scenario: Unknown flags rejected
- **WHEN** the flags byte has a bit set that this reader does not recognize
- **THEN** `Open` throws `InvalidDataException` naming the flag bit

### Requirement: Reject baseline-dependent v1 files
In v1 the resolver SHALL reject files where the baseline id or baseline version is non-zero, with an error message indicating the file requires a baseline that v1 readers do not support.

#### Scenario: Non-zero baseline id
- **WHEN** `Open` is called on a v1 file with a non-zero baseline id
- **THEN** it throws `InvalidDataException` explaining the reader does not support baselines

### Requirement: Project selection from manifest
When the `.glocontext` manifest contains multiple compilations, `Resolve(projectName)` SHALL select by project name. When `projectName` is null, it SHALL select the first compilation. When the named project is not present, it SHALL throw with a list of available names. When the manifest is empty, it SHALL throw `InvalidOperationException`.

#### Scenario: Default to first compilation
- **WHEN** `Resolve(null)` is called on a manifest with multiple compilations
- **THEN** the first compilation is returned

#### Scenario: Named project not found
- **WHEN** `Resolve("Missing.csproj")` is called
- **THEN** it throws with an error message that lists the available project names

#### Scenario: Empty manifest
- **WHEN** `Resolve` is called on a manifest with zero compilations
- **THEN** it throws `InvalidOperationException`

### Requirement: Resolved references match compacted blobs
The resolver SHALL materialize each `MetadataReference` from the `refs/<hash>.dll` blob identified in the manifest. Reference order in the result SHALL match the manifest order. Display names and alias lists SHALL be preserved from the manifest.

#### Scenario: Reference order preserved
- **WHEN** `Resolve` is called
- **THEN** the returned references appear in the same order as the manifest's `references` array for the selected compilation

#### Scenario: Aliases preserved
- **WHEN** a compilation references an assembly with external aliases in the manifest
- **THEN** the returned `MetadataReference` has those aliases set

### Requirement: --complog flag auto-detects .glocontext vs .complog
The existing `--complog` option on `process`, `verify`, and `render`, and the `ComplogPath` property on `GloSharpProcessorOptions`, SHALL auto-detect the file format by reading its first bytes. A file beginning with `GLOCTX` SHALL be routed to `GloContextResolver`. A file beginning with the zip magic (`PK\x03\x04`) SHALL be routed to `ComplogResolver`. Any other content SHALL produce an error that names the expected formats.

#### Scenario: Route .glocontext
- **WHEN** `glosharp process file.cs --complog ctx.glocontext` is run
- **THEN** the processor opens the file via `GloContextResolver` and proceeds

#### Scenario: Route .complog
- **WHEN** `glosharp process file.cs --complog build.complog` is run
- **THEN** the processor opens the file via `ComplogResolver` and proceeds

#### Scenario: Unrecognized format
- **WHEN** `--complog` points at a file that is neither a `.glocontext` nor a zip
- **THEN** the CLI writes an error to stderr naming both expected formats and exits non-zero
