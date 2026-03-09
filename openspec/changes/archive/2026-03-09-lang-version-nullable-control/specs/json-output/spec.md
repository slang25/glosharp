## MODIFIED Requirements

### Requirement: Meta object in JSON
The `meta` object SHALL contain: `targetFramework` (string), `packages` (array of `{name, version}` objects), `compileSucceeded` (boolean), `sdk` (string or null), `langVersion` (string or null), and `nullable` (string or null). The `packages` array SHALL be populated from `#:package` directives when present, or from `project.assets.json` when using project-based resolution. The `sdk` field SHALL contain the SDK identifier from `#:sdk` directive, or null when not specified. The `langVersion` field SHALL contain the authored language version string when a `// @langVersion` marker is present, or null when using the default. The `nullable` field SHALL contain the authored nullable context string when a `// @nullable` marker is present, or null when using the default.

#### Scenario: Successful compilation meta
- **WHEN** compilation succeeds with no unexpected errors
- **THEN** `meta.compileSucceeded` is `true` and `meta.targetFramework` reflects the resolved framework

#### Scenario: Meta with file-based app packages
- **WHEN** source contains `#:package Newtonsoft.Json@13.0.3` and `#:package Serilog@3.1.1`
- **THEN** `meta.packages` is `[{"name": "Newtonsoft.Json", "version": "13.0.3"}, {"name": "Serilog", "version": "3.1.1"}]`

#### Scenario: Meta with SDK specified
- **WHEN** source contains `#:sdk Microsoft.NET.Sdk.Web`
- **THEN** `meta.sdk` is `"Microsoft.NET.Sdk.Web"`

#### Scenario: Meta without SDK
- **WHEN** source contains no `#:sdk` directive
- **THEN** `meta.sdk` is null or omitted from the JSON output

#### Scenario: Meta with no packages
- **WHEN** source has no `#:package` directives and no project-based resolution
- **THEN** `meta.packages` is `[]`

#### Scenario: Meta with language version
- **WHEN** source contains `// @langVersion: 12`
- **THEN** `meta.langVersion` is `"12"`

#### Scenario: Meta with nullable context
- **WHEN** source contains `// @nullable: disable`
- **THEN** `meta.nullable` is `"disable"`

#### Scenario: Meta without language version or nullable
- **WHEN** source contains no `// @langVersion` or `// @nullable` markers
- **THEN** `meta.langVersion` is null and `meta.nullable` is null (or omitted)
