## Context

Twohash processes C# documentation snippets through Roslyn to extract hover info, errors, completions, and highlights. The current architecture is stateless: each `TwohashProcessor.ProcessAsync()` call resolves references from scratch and creates a fresh `CSharpCompilation`. The CLI spawns a new process per invocation from the Node bridge.

For a documentation site with 50+ snippets from the same project, this means:
- 50× framework reference assembly discovery (FrameworkResolver)
- 50× project.assets.json parsing (ProjectAssetsResolver)
- 50× MetadataReference creation from the same DLL paths
- 50× Roslyn compilation with identical references

The `verify` command already processes multiple files in one process but still resolves references independently per file.

## Goals / Non-Goals

**Goals:**
- Eliminate redundant reference resolution when multiple snippets share the same compilation context
- Enable cross-build result caching so unchanged snippets skip Roslyn entirely on rebuild
- Keep caching fully opt-in — no behavior change without `--cache-dir`
- Support the Node bridge as a transparent passthrough

**Non-Goals:**
- Cache eviction or cleanup (users manage their cache directory, e.g., via `rm -rf`)
- Distributed or shared caching across machines
- Incremental Roslyn compilation (reusing syntax trees across different source inputs)
- Caching within Shiki/EC plugins (they delegate to the Node bridge)
- Watch mode or file-system notification-based invalidation

## Decisions

### 1. Two-layer cache architecture

**Decision**: Implement two independent cache layers — in-process compilation context cache and disk-based result cache.

**Rationale**: These solve different problems. The compilation context cache helps the `verify` command (many files, one process, same references). The disk result cache helps the Node bridge (many processes, one per snippet, results persisted across builds). Neither alone covers both cases.

**Alternative considered**: Single disk-only cache. Rejected because the `verify` command processes files in a loop within one process — disk I/O for reference caching would be slower than keeping MetadataReferences in memory.

### 2. Cache key composition for disk cache

**Decision**: The disk cache key is a SHA256 hash of: `twohash_version + "\0" + target_framework + "\0" + sorted_packages_json + "\0" + project_path + "\0" + source_code`.

**Rationale**: This captures every input that affects the output. The version component ensures cache invalidation on twohash upgrades. Sorting packages makes the key deterministic regardless of declaration order. The null byte separator prevents ambiguous concatenation.

**Alternative considered**: Using file modification timestamps. Rejected because timestamps don't reflect content changes (e.g., `touch` without edit) and don't work for stdin input.

### 3. Flat file cache layout

**Decision**: Cache entries are stored as `<cache-dir>/<sha256-hex>.json` — one file per cached result. No subdirectories, no index file.

**Rationale**: Simple, debuggable, no corruption risk from index desync. File systems handle thousands of files in a directory well enough for documentation builds. Cache lookup is a single file existence check + read.

**Alternative considered**: SQLite database. Rejected as over-engineered for this use case — adds a dependency and complexity for no meaningful benefit at documentation-site scale (hundreds to low thousands of entries).

### 4. CompilationContext cache keyed by reference inputs

**Decision**: The in-process compilation context cache keys on `(targetFramework, sorted package list hash, projectAssetsPath)`. The cached value is the resolved `MetadataReference[]` array.

**Rationale**: These three inputs fully determine which DLLs are loaded. The `verify` command typically uses the same project for all files, so this cache has a near-100% hit rate after the first file.

### 5. Cache is opt-in via --cache-dir

**Decision**: No caching without an explicit `--cache-dir` flag. No default cache location.

**Rationale**: Caching introduces a correctness risk (stale results). Making it opt-in means users consciously choose the trade-off. CI environments can set it explicitly. A default cache location (e.g., `~/.cache/twohash`) would add surprise behavior.

**Alternative considered**: Default to `~/.cache/twohash` with `--no-cache` to disable. Rejected because silent caching in a build tool can mask real compilation errors when dependencies change.

### 6. CompilationContext cache is always on for verify

**Decision**: The in-process compilation context cache is always active during `verify` — no flag needed. It's purely a performance optimization with no correctness risk (same process, same inputs, same outputs).

**Rationale**: The `verify` command already processes files in a loop. Reusing resolved references within that loop is a straightforward optimization with no observable behavior change.

## Risks / Trade-offs

**[Stale cache results]** → The cache key includes twohash version and all compilation inputs. Users who change NuGet packages must rebuild the cache. Documentation should note that `--cache-dir` contents can be safely deleted at any time.

**[Disk space growth]** → Each cached result is a JSON file (typically 1-10 KB). A site with 500 snippets uses ~5 MB. No automatic cleanup — users delete the directory. This is acceptable for build-time tooling.

**[File-based app cache key complexity]** → File-based apps resolve through the SDK (`dotnet build`), making it hard to compute a stable cache key before resolution. Decision: for file-based apps, include the raw `#:` directive lines in the cache key (not the resolved output). If the directives haven't changed, the resolution result is the same.

**[Concurrent writes]** → Multiple parallel CLI processes could write to the same cache directory. Mitigation: write to a temp file then atomic rename (`File.Move` with overwrite). JSON files are self-contained, so partial reads are detectable (invalid JSON).
