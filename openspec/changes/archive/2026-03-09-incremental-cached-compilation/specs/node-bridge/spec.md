## ADDED Requirements

### Requirement: Cache-dir option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `cacheDir` property specifying a directory for disk-based result caching, applied to all `process()` calls on the instance.

#### Scenario: Instance-level cache directory
- **WHEN** `createGloSharp({ cacheDir: '.glosharp-cache' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--cache-dir .glosharp-cache`

### Requirement: Cache-dir option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `cacheDir` property that overrides the instance-level `cacheDir` for a single call.

#### Scenario: Per-call cache directory override
- **WHEN** `glosharp.process({ code: '...', cacheDir: './other-cache' })` is called on an instance with a different `cacheDir`
- **THEN** the CLI is spawned with `--cache-dir ./other-cache`

#### Scenario: Per-call cache directory without instance default
- **WHEN** `glosharp.process({ code: '...', cacheDir: '.glosharp-cache' })` is called on an instance without `cacheDir`
- **THEN** the CLI is spawned with `--cache-dir .glosharp-cache`

#### Scenario: No cache directory
- **WHEN** `glosharp.process({ code: '...' })` is called on an instance without `cacheDir`
- **THEN** the CLI is spawned without `--cache-dir` (existing behavior unchanged)
