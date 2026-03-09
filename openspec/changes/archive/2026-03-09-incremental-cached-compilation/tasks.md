## 1. Core Models and Options

- [x] 1.1 Add `CacheDir` property to `TwohashProcessorOptions` in `Models.cs`
- [x] 1.2 Add assembly version helper to compute twohash version string for cache keys

## 2. Compilation Context Cache

- [x] 2.1 Create `CompilationContextCache` class with `GetOrAdd(key, factory)` pattern keyed by (targetFramework, packages, projectAssetsPath/hash)
- [x] 2.2 Integrate `CompilationContextCache` into `TwohashProcessor` reference resolution — wrap FrameworkResolver, ProjectAssetsResolver, and FileBasedAppResolver calls
- [x] 2.3 Wire `CompilationContextCache` as shared instance in `verify` command loop
- [x] 2.4 Add tests for compilation context cache hit/miss and key differentiation

## 3. Disk-Based Result Cache

- [x] 3.1 Create `ResultCache` class with `TryGet(key)` and `Set(key, result)` methods, flat file layout at `<cache-dir>/<sha256>.json`
- [x] 3.2 Implement cache key computation: SHA256 of (version + framework + sorted packages JSON + project path + source code) with null byte separators
- [x] 3.3 Implement atomic writes (write to temp file, then `File.Move` with overwrite)
- [x] 3.4 Handle corrupt cache files (invalid JSON → treat as miss, overwrite)
- [x] 3.5 Auto-create cache directory on first write
- [x] 3.6 Integrate `ResultCache` into `TwohashProcessor.ProcessAsync()` — check cache before processing, write on miss
- [x] 3.7 Add tests for result cache: miss → write, hit → return, corrupt → reprocess, version invalidation, package order determinism

## 4. CLI Integration

- [x] 4.1 Add `--cache-dir <path>` option to `process` command in `Program.cs`
- [x] 4.2 Add `--cache-dir <path>` option to `verify` command in `Program.cs`
- [x] 4.3 Pass `CacheDir` through to `TwohashProcessorOptions` in both commands
- [x] 4.4 Add CLI integration tests: process with cache dir, verify with cache dir

## 5. Node Bridge

- [x] 5.1 Add `cacheDir?: string` to `TwohashOptions` and `TwohashProcessOptions` in `types.ts`
- [x] 5.2 Pass `cacheDir` as `--cache-dir` argument in CLI spawn logic in `twohash.ts` (instance-level default, per-call override)
- [x] 5.3 Add tests for cacheDir passthrough in Node bridge
