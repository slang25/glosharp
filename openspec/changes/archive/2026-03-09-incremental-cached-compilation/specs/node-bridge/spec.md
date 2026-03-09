## ADDED Requirements

### Requirement: Cache-dir option in TwohashOptions
The `TwohashOptions` interface SHALL accept an optional `cacheDir` property specifying a directory for disk-based result caching, applied to all `process()` calls on the instance.

#### Scenario: Instance-level cache directory
- **WHEN** `createTwohash({ cacheDir: '.twohash-cache' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--cache-dir .twohash-cache`

### Requirement: Cache-dir option in TwohashProcessOptions
The `TwohashProcessOptions` interface SHALL accept an optional `cacheDir` property that overrides the instance-level `cacheDir` for a single call.

#### Scenario: Per-call cache directory override
- **WHEN** `twohash.process({ code: '...', cacheDir: './other-cache' })` is called on an instance with a different `cacheDir`
- **THEN** the CLI is spawned with `--cache-dir ./other-cache`

#### Scenario: Per-call cache directory without instance default
- **WHEN** `twohash.process({ code: '...', cacheDir: '.twohash-cache' })` is called on an instance without `cacheDir`
- **THEN** the CLI is spawned with `--cache-dir .twohash-cache`

#### Scenario: No cache directory
- **WHEN** `twohash.process({ code: '...' })` is called on an instance without `cacheDir`
- **THEN** the CLI is spawned without `--cache-dir` (existing behavior unchanged)
