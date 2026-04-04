## ADDED Requirements

### Requirement: Cache-dir option for process command
The CLI SHALL accept a `--cache-dir <path>` option on the `process` command specifying a directory for disk-based result caching.

#### Scenario: Process with cache directory
- **WHEN** `glosharp process snippet.cs --cache-dir .glosharp-cache` is run
- **THEN** the CLI checks the cache directory for a matching result before processing, and writes the result to the cache on a miss

#### Scenario: Process without cache directory
- **WHEN** `glosharp process snippet.cs` is run without `--cache-dir`
- **THEN** no disk caching occurs (existing behavior unchanged)

### Requirement: Cache-dir option for verify command
The CLI SHALL accept a `--cache-dir <path>` option on the `verify` command. Each file's result SHALL be individually cached.

#### Scenario: Verify with cache directory
- **WHEN** `glosharp verify samples/ --cache-dir .glosharp-cache` is run
- **THEN** each file checks the cache before processing and writes results on cache miss

#### Scenario: Verify reuses cached results on rebuild
- **WHEN** `glosharp verify samples/ --cache-dir .glosharp-cache` is run a second time with no source changes
- **THEN** all files hit the cache and no Roslyn compilation occurs
