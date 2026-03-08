## ADDED Requirements

### Requirement: Compile C# source with Roslyn
The system SHALL parse C# source using `CSharpSyntaxTree.ParseText()` and create a `CSharpCompilation` with framework reference assemblies. The compilation SHALL use `OutputKind.ConsoleApplication` to support top-level statements.

#### Scenario: Compile simple top-level statement
- **WHEN** source is `var x = 42; Console.WriteLine(x);`
- **THEN** compilation succeeds with no errors

#### Scenario: Compile with implicit usings
- **WHEN** source uses `Console.WriteLine` without explicit `using System;`
- **THEN** the system adds global usings for the default .NET implicit using set and compilation succeeds

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

### Requirement: Extract hover information at queried positions
The system SHALL resolve the syntax node at each hover query position, obtain symbol info via `SemanticModel.GetSymbolInfo()` or `SemanticModel.GetDeclaredSymbol()`, and produce structured hover data including display parts, documentation, and symbol kind.

#### Scenario: Local variable hover
- **WHEN** a hover query targets a local variable `x` of type `int`
- **THEN** the hover text is `(local variable) int x` with structured parts containing keyword, text, and localName kinds

#### Scenario: Method call hover
- **WHEN** a hover query targets `Console.WriteLine`
- **THEN** the hover text shows the method signature with overload count and structured parts

#### Scenario: Hover with XML doc comment
- **WHEN** a hover query targets a symbol that has XML documentation
- **THEN** the hover includes the `docs` field with the extracted documentation text

### Requirement: Extract structured display parts
The system SHALL use `ISymbol.ToDisplayParts()` to produce an array of display parts, each with a `kind` (mapped from `SymbolDisplayPartKind`) and `text` value.

#### Scenario: Display parts for typed variable
- **WHEN** hover is extracted for `string greeting`
- **THEN** parts include entries with kinds `punctuation`, `text`, `keyword`, `space`, `localName` mapping to Roslyn's `SymbolDisplayPartKind`

### Requirement: Extract compiler diagnostics
The system SHALL collect all diagnostics from `Compilation.GetDiagnostics()` at error, warning, and info severity levels, with line, character, length, error code, message, and severity.

#### Scenario: Syntax error diagnostic
- **WHEN** source has a missing semicolon
- **THEN** the system produces a diagnostic with code `CS1002`, severity `error`, and the correct position

#### Scenario: Expected vs unexpected errors
- **WHEN** an error matches an `// @errors:` declaration
- **THEN** the diagnostic is marked as `expected: true` in the output

### Requirement: Handle overloaded methods
The system SHALL detect overloaded methods and include the overload count in the hover text and as a separate `overloadCount` field.

#### Scenario: Method with overloads
- **WHEN** hover targets `Console.WriteLine` which has 18 overloads
- **THEN** hover text includes `(+ 17 overloads)` and `overloadCount` is `18`

### Requirement: Handle nullable annotations
The system SHALL respect the nullable context and display accurate nullable annotations (`string` vs `string?`) in hover text.

#### Scenario: Nullable parameter display
- **WHEN** hover targets a parameter of type `string?` in a nullable-enabled context
- **THEN** hover text shows `string?` not `string`
