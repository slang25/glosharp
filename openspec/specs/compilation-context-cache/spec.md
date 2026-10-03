# compilation-context-cache Specification

## Purpose
Reuse resolved compilation references in-process across snippets.

## Requirements
### Requirement: In-process MetadataReference caching
The system SHALL cache resolved `MetadataReference[]` arrays in memory, keyed by compilation context (target framework, sorted package list, project assets path). Subsequent calls with the same compilation context within the same process SHALL reuse the cached references.

#### Scenario: Second file reuses references in verify
- **WHEN** `glosharp verify samples/` processes file A then file B, both using the same `--project` and `--framework`
- **THEN** reference resolution (FrameworkResolver, ProjectAssetsResolver) runs only for file A; file B reuses the cached MetadataReference array

#### Scenario: Different frameworks get separate caches
- **WHEN** file A targets `net8.0` and file B targets `net9.0` within the same process
- **THEN** each framework resolves independently and both are cached separately

### Requirement: Whole complog/.glocontext resolutions are cached
For `--complog` (raw complog or .glocontext) the cache SHALL hold the complete resolution — references, compilation options, parse options, target framework and inferred packages — keyed by full path, selected project, file size and last-write time. A cache hit SHALL NOT reopen or re-read the file.

#### Scenario: verify over many files with one complog
- **WHEN** `glosharp verify` processes many files against the same `--complog`
- **THEN** the complog/.glocontext is opened and resolved once

### Requirement: Thread-safe cache
The cache SHALL be safe to use from concurrent callers sharing one `GloSharpProcessor`. Concurrent requests for the same key SHALL share a single resolution; a resolution that throws SHALL NOT be cached.

#### Scenario: Parallel processing
- **WHEN** several snippets are processed concurrently with `Task.WhenAll` through one processor
- **THEN** each context is resolved once and no cache corruption occurs

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
