## ADDED Requirements

### Requirement: Parse langVersion marker
The system SHALL recognize `// @langVersion: <value>` comment lines as configuration markers. The value SHALL be case-insensitive and support numeric versions (`7`, `7.1`, `8`, `9`, `10`, `11`, `12`, `13`, `14`, and the `N.0` spellings) and named versions (`latest`, `latestmajor`, `preview`, `default`) — anything the compiler's `-langversion` accepts. The marker line SHALL be stripped from processed output and excluded from compilation code.

#### Scenario: Numeric language version
- **WHEN** source contains `// @langVersion: 12`
- **THEN** the system records a language version of `"12"` and the marker line is removed from processed output

#### Scenario: Named language version
- **WHEN** source contains `// @langVersion: preview`
- **THEN** the system records a language version of `"preview"` and the marker line is removed from processed output

#### Scenario: Case insensitive
- **WHEN** source contains `// @langVersion: Latest`
- **THEN** the system records a language version of `"latest"` (normalized to lowercase)

#### Scenario: Last one wins
- **WHEN** source contains `// @langVersion: 12` followed later by `// @langVersion: 11`
- **THEN** the system uses language version `"11"`

### Requirement: Parse nullable marker
The system SHALL recognize `// @nullable: <value>` comment lines as configuration markers. Valid values SHALL be `enable`, `disable`, `warnings`, and `annotations` (case-insensitive). The marker line SHALL be stripped from processed output and excluded from compilation code.

#### Scenario: Nullable enable
- **WHEN** source contains `// @nullable: enable`
- **THEN** the system records nullable context as `"enable"` and the marker line is removed from processed output

#### Scenario: Nullable disable
- **WHEN** source contains `// @nullable: disable`
- **THEN** the system records nullable context as `"disable"` and the marker line is removed from processed output

#### Scenario: Nullable warnings
- **WHEN** source contains `// @nullable: warnings`
- **THEN** the system records nullable context as `"warnings"` and the marker line is removed from processed output

#### Scenario: Nullable annotations
- **WHEN** source contains `// @nullable: annotations`
- **THEN** the system records nullable context as `"annotations"` and the marker line is removed from processed output

#### Scenario: Case insensitive
- **WHEN** source contains `// @nullable: Disable`
- **THEN** the system records nullable context as `"disable"` (normalized to lowercase)

### Requirement: Apply language version to compilation
The system SHALL map the parsed `@langVersion` value to a Roslyn `LanguageVersion` enum value and apply it to `CSharpParseOptions`. When no `@langVersion` marker is present, the system SHALL default to `LanguageVersion.Latest`.

#### Scenario: Language version applied
- **WHEN** source contains `// @langVersion: 12` and uses C# 12 features
- **THEN** compilation uses `LanguageVersion.CSharp12` and the features compile successfully

#### Scenario: Language version restricts features
- **WHEN** source contains `// @langVersion: 7` and uses C# 12 features (e.g., collection expressions)
- **THEN** compilation produces diagnostics for unsupported language features

#### Scenario: Default language version
- **WHEN** source contains no `// @langVersion` marker
- **THEN** compilation uses `LanguageVersion.Latest`

### Requirement: Apply nullable context to compilation
The system SHALL map the parsed `@nullable` value to a Roslyn `NullableContextOptions` enum value and apply it to `CSharpCompilationOptions`. When no `@nullable` marker is present, the system SHALL default to `NullableContextOptions.Enable`.

#### Scenario: Nullable disabled
- **WHEN** source contains `// @nullable: disable` and assigns `null` to a `string` variable
- **THEN** compilation produces no nullable warnings

#### Scenario: Nullable enabled
- **WHEN** source contains `// @nullable: enable` and assigns `null` to a `string` variable
- **THEN** compilation produces nullable warning CS8600

#### Scenario: Default nullable context
- **WHEN** source contains no `// @nullable` marker
- **THEN** compilation uses `NullableContextOptions.Enable`

### Requirement: Invalid values produce diagnostic
The system SHALL produce an error entry when a `@langVersion` or `@nullable` marker (or the corresponding config value) has an unrecognized value. The error message SHALL list the valid values. The code SHALL be `GS0001` for language version and `GS0002` for nullable (formerly `TH0001`/`TH0002`); `sourceLine` points at the marker line (0 for config values).

#### Scenario: Invalid language version
- **WHEN** source contains `// @langVersion: 99`
- **THEN** the errors array contains an entry with a message indicating `"99"` is not a valid language version and listing valid options

#### Scenario: Invalid nullable value
- **WHEN** source contains `// @nullable: sometimes`
- **THEN** the errors array contains an entry with a message indicating `"sometimes"` is not a valid nullable context and listing valid options

### Requirement: Markers work with completions extraction
The system SHALL apply the parsed language version and nullable context when creating the compilation used for completion extraction (the `^|` marker path).

#### Scenario: Completions with language version
- **WHEN** source contains `// @langVersion: 12` and a `^|` completion marker
- **THEN** completion extraction uses `LanguageVersion.CSharp12` for its compilation

### Requirement: Meta reports effective values
`meta.langVersion` and `meta.nullable` SHALL always report the effective setting after precedence (marker > config > complog > default), using the marker spelling (`"12"`, `"latest"`, `"enable"`, ...). With no marker or config they report the defaults `"latest"` and `"enable"` (or the complog project's values).

### Requirement: Target framework preprocessor symbols
Outside complog mode the parse options SHALL define the preprocessor symbols the .NET SDK defines for the effective target framework (`NET`, `NETn_m`, `NETn_m_OR_GREATER`, `NETCOREAPP`, `NETCOREAPPx_y_OR_GREATER`, `NETSTANDARD...`, `NETFRAMEWORK...`, platform symbols) plus `TRACE`.

#### Scenario: Version-conditional code
- **WHEN** a `net8.0` snippet contains `#if NET8_0_OR_GREATER ... #else <invalid code> #endif`
- **THEN** the `#if` branch is compiled and the snippet succeeds

### Requirement: Complog options are inherited
In complog/.glocontext mode the compilation SHALL start from the project's recorded `CSharpCompilationOptions`/`CSharpParseOptions` (AllowUnsafe, defines, NoWarn/specific diagnostic options, warning level, features, ...) and override only: output kind (console application, no main type), nullable and language version (when set by marker or config), parse kind (regular), documentation mode (at least `Parse`) and strong-name signing (cleared). When the references include ASP.NET Core and no `implicitUsings` config is set, the Web SDK implicit usings are added.

#### Scenario: Unsafe code in an AllowUnsafeBlocks project
- **WHEN** a snippet with an `unsafe` block is processed with a complog of a project that sets `AllowUnsafeBlocks`
- **THEN** no CS0227 is reported
