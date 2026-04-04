## ADDED Requirements

### Requirement: In-process MetadataReference caching
The system SHALL cache resolved `MetadataReference[]` arrays in memory, keyed by compilation context (target framework, sorted package list, project assets path). Subsequent calls with the same compilation context within the same process SHALL reuse the cached references.

#### Scenario: Second file reuses references in verify
- **WHEN** `glosharp verify samples/` processes file A then file B, both using the same `--project` and `--framework`
- **THEN** reference resolution (FrameworkResolver, ProjectAssetsResolver) runs only for file A; file B reuses the cached MetadataReference array

#### Scenario: Different frameworks get separate caches
- **WHEN** file A targets `net8.0` and file B targets `net9.0` within the same process
- **THEN** each framework resolves independently and both are cached separately

### Requirement: Compilation context cache is always active
The in-process compilation context cache SHALL be active whenever `GloSharpProcessor` is used. No flag is required to enable it.

#### Scenario: Single process call benefits from cache
- **WHEN** `GloSharpProcessor` is instantiated and used to process multiple snippets
- **THEN** the compilation context cache is available without any explicit configuration

### Requirement: Compilation context key includes project assets content
When a project path is specified, the cache key SHALL include a hash of the `project.assets.json` content (or the project path as fallback) to detect dependency changes.

#### Scenario: Updated project assets invalidates context cache
- **WHEN** `project.assets.json` changes between two `verify` runs within the same process (e.g., after a restore)
- **THEN** the compilation context cache key differs and references are re-resolved

### Requirement: File-based app context caching
For file-based app mode, the compilation context cache key SHALL include the sorted `#:` directive lines. Files with identical directives SHALL share cached references.

#### Scenario: Two files with same directives share context
- **WHEN** file A and file B both contain `#:package Newtonsoft.Json@13.0.3` with the same framework
- **THEN** reference resolution runs once and both files use the cached MetadataReference array
