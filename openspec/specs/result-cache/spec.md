## ADDED Requirements

### Requirement: Disk-based result caching by content hash
The system SHALL cache `GloSharpResult` JSON to disk when a cache directory is configured. The cache key SHALL be the SHA256 hash of: the glosharp assembly version, the canonical JSON serialisation of the complete effective `GloSharpProcessorOptions` record (after config merge, excluding only `cacheDir`), fingerprints of on-disk inputs referenced by path, and the source code (which includes any `#:` directives), concatenated with null byte separators.

Key completeness SHALL be structural: because the whole options record is serialised, every option (region, target framework, project, complog path and project, implicit usings, language version, nullable context, no-restore, ...) is part of the key, and options added later are included automatically. `sourceFilePath` is only included when the source has `#:` directives (it only influences file-based app resolution).

#### Scenario: Cache miss writes result to disk
- **WHEN** `GloSharpProcessor.ProcessAsync()` completes with a cache directory configured and no matching cache file exists
- **THEN** the result JSON SHALL be written to `<cache-dir>/<sha256-hex>.json`

#### Scenario: Cache hit returns stored result
- **WHEN** `GloSharpProcessor.ProcessAsync()` is called with a cache directory configured and a matching cache file exists at `<cache-dir>/<sha256-hex>.json`
- **THEN** the cached JSON SHALL be deserialized and returned without invoking Roslyn compilation or reference resolution (including any `dotnet` process for file-based apps)

#### Scenario: Compilation built lazily after a hit
- **WHEN** a cache hit is returned by `ProcessWithContextAsync()` and the caller (e.g. `render`) accesses `Compilation` or `SyntaxTree`
- **THEN** references are resolved and the snippet compiled on first access only

#### Scenario: Different regions of one file are cached separately
- **WHEN** the same file is processed with `--region a` and then `--region b` against the same cache directory
- **THEN** the second call is a cache miss and returns region `b`'s code

#### Scenario: Config compiler options are part of the key
- **WHEN** the same source is processed with `nullable: enable` and then `nullable: disable` (or a different `langVersion`, `implicitUsings`, `complogProject`)
- **THEN** the keys differ and each result reflects its own options

#### Scenario: No cache directory configured
- **WHEN** `GloSharpProcessor.ProcessAsync()` is called without a cache directory in options
- **THEN** no disk caching SHALL occur and processing proceeds as before

### Requirement: Cache key fingerprints path-referenced inputs
When a project path or complog/.glocontext path is configured, the key SHALL include a fingerprint (full path, size and last-write time) of the project's `project.assets.json` and of the complog/.glocontext file, so a re-restore or a rebuilt context invalidates cached results.

#### Scenario: Re-restored project invalidates cache
- **WHEN** `project.assets.json` is rewritten (e.g. after a package bump) between two runs with the same cache directory
- **THEN** the second run is a cache miss

#### Scenario: Rebuilt complog invalidates cache
- **WHEN** the complog or .glocontext file is regenerated at the same path
- **THEN** the next run is a cache miss

Known limitation: the installed SDK's targeting-pack patch version and floating `#:package` versions are not fingerprinted.

### Requirement: Cache key includes glosharp version
The cache key computation SHALL include the glosharp assembly version so that cached results from a different version are not reused.

#### Scenario: Version change invalidates cache
- **WHEN** a cache file exists from glosharp version 1.0.0 and glosharp version 1.1.0 processes the same source
- **THEN** the cache key differs and the system processes from scratch, writing a new cache entry

### Requirement: Atomic cache writes
Cache files SHALL be written atomically to prevent corruption from concurrent CLI processes writing to the same cache directory.

#### Scenario: Concurrent writes do not corrupt cache
- **WHEN** two CLI processes write to the same cache directory simultaneously
- **THEN** each writes to a temporary file first, then renames to the final path, ensuring only complete JSON files exist in the cache directory

### Requirement: Graceful handling of corrupt cache files
The system SHALL handle corrupt or invalid cache files gracefully by treating them as cache misses.

#### Scenario: Corrupt cache file treated as miss
- **WHEN** a cache file exists at the expected path but contains invalid JSON, or cannot be read because it is being replaced concurrently
- **THEN** the system SHALL process from scratch and overwrite the corrupt file with a valid result

### Requirement: Cache directory auto-creation
The system SHALL create the cache directory (including parent directories) if it does not exist when writing a cache entry.

#### Scenario: Cache directory created on first write
- **WHEN** the configured cache directory does not exist and a cache miss occurs
- **THEN** the directory SHALL be created and the cache file written successfully
