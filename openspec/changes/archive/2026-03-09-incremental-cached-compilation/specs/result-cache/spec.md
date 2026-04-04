## ADDED Requirements

### Requirement: Disk-based result caching by content hash
The system SHALL cache `GloSharpResult` JSON to disk when a cache directory is configured. The cache key SHALL be the SHA256 hash of: glosharp assembly version, target framework, sorted package list (as JSON), project path, and source code, concatenated with null byte separators.

#### Scenario: Cache miss writes result to disk
- **WHEN** `GloSharpProcessor.ProcessAsync()` completes with a cache directory configured and no matching cache file exists
- **THEN** the result JSON SHALL be written to `<cache-dir>/<sha256-hex>.json`

#### Scenario: Cache hit returns stored result
- **WHEN** `GloSharpProcessor.ProcessAsync()` is called with a cache directory configured and a matching cache file exists at `<cache-dir>/<sha256-hex>.json`
- **THEN** the cached JSON SHALL be deserialized and returned without invoking Roslyn compilation or reference resolution

#### Scenario: No cache directory configured
- **WHEN** `GloSharpProcessor.ProcessAsync()` is called without a cache directory in options
- **THEN** no disk caching SHALL occur and processing proceeds as before

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
- **WHEN** a cache file exists at the expected path but contains invalid JSON
- **THEN** the system SHALL process from scratch and overwrite the corrupt file with a valid result

### Requirement: Cache key determinism for packages
The package list in the cache key SHALL be sorted by package name to produce a deterministic key regardless of declaration order.

#### Scenario: Different package order produces same key
- **WHEN** source A declares `#:package Foo` then `#:package Bar` and source B declares `#:package Bar` then `#:package Foo`, but the source code is otherwise identical
- **THEN** both produce the same cache key

### Requirement: Cache directory auto-creation
The system SHALL create the cache directory (including parent directories) if it does not exist when writing a cache entry.

#### Scenario: Cache directory created on first write
- **WHEN** the configured cache directory does not exist and a cache miss occurs
- **THEN** the directory SHALL be created and the cache file written successfully
