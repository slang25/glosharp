## Context

Twohash currently resolves compilation references through a tiered approach in `TwohashProcessor`:
1. `--project` flag → `ProjectAssetsResolver` reads `project.assets.json`
2. `#:` file directives → `FileBasedAppResolver` runs `dotnet build` then reads generated assets
3. Fallback → `FrameworkResolver` loads SDK reference assemblies only

All three tiers require either the .NET SDK installed or a restored project on the local machine. This is a barrier for documentation builds that run on separate CI machines or in pure Node.js environments.

[complog](https://github.com/jaredpar/complog) by Jared Parsons (Roslyn compiler team) captures the full compilation state — all references, compiler options, source files — into a portable `.complog` file. The `Basic.CompilerLog.Util` NuGet package can recreate `CSharpCompilation` objects from these files with no SDK needed.

## Goals / Non-Goals

**Goals:**
- Accept `.complog` files as a compilation source, bypassing all existing reference resolution
- Support selecting a specific project from multi-project complogs
- Integrate cleanly with existing caching (result cache, compilation context cache)
- Propagate complog metadata (framework, packages) into `TwohashMeta`
- Surface complog support through CLI, Node bridge, and config file

**Non-Goals:**
- Creating complog files (that's the complog CLI tool's job)
- Supporting binary log (`.binlog`) files directly — users create complogs from binlogs
- Modifying the complog library or forking it
- Making complog the default resolution path — it's an opt-in tier for CI-separated builds

## Decisions

### 1. Extract references from complog, don't reuse its Compilation directly

**Options:**
- (a) Reuse the `Compilation` object from complog and replace the source tree
- (b) Extract `MetadataReference[]` and `CompilationOptions` from the complog, then create a fresh compilation with the user's processed source

**Decision: (b) — Extract references and options**

The complog's `Compilation` contains the original project's source trees, which are irrelevant to the user's snippet. We need a compilation with twohash's processed source (markers stripped, global usings added). Extracting references and options keeps the complog resolver aligned with how `FrameworkResolver` and `ProjectAssetsResolver` work — they all produce references that `TwohashProcessor` assembles into a compilation. This also means the existing `CompilationContextCache` can cache complog-sourced references naturally.

### 2. Project selection defaults to first C# compilation

**Options:**
- (a) Require `--complog-project` always
- (b) Default to the first C# compilation, error if multiple exist without selection
- (c) Default to the first C# compilation, with optional `--complog-project` override

**Decision: (c) — Default to first, optional override**

Most documentation projects will have a single compilation in their complog. Requiring the project name for the common case adds unnecessary friction. When multiple compilations exist, `--complog-project` allows selection. If the complog contains no C# compilations, error with a clear message.

### 3. Mutual exclusivity with --project and file-based app directives

When `--complog` is specified:
- `--project` is rejected (CLI validation error) — both provide compilation context
- `#:` file directives in source are still parsed and stripped from output, but they are NOT used for resolution — the complog provides all references
- `--framework` is ignored (the complog's framework is authoritative)
- `--no-restore` is irrelevant (no restore happens)

This keeps the mental model simple: complog replaces the entire reference resolution pipeline.

### 4. Cache key for complog-sourced results

The result cache key currently includes: twohash version, framework, packages, project path, source code. For complog mode, the key SHALL include: twohash version, complog file path, complog project name (or empty), source code. The complog file path serves as a proxy for the compilation state — if the complog is regenerated, the path is typically the same but the mtime/content changes. For correctness, we include a hash of the complog file's last-write-time rather than the file content (complogs can be large).

The compilation context cache key for complog-sourced references SHALL be: complog file path + complog project name + complog last-write-time. This allows multiple snippets using the same complog to share cached references.

### 5. ComplogResolver as a new class in TwoHash.Core

`ComplogResolver` follows the same pattern as the other resolvers:
- Input: complog file path, optional project name
- Output: `ComplogResolutionResult` containing `List<MetadataReference>`, `CSharpCompilationOptions`, `CSharpParseOptions`, target framework string, and package list
- Internally uses `CompilerCallReaderUtil.Create()` and `CompilationData.GetCompilationAfterGenerators()`
- Extracts references via `compilation.References`, options via `compilation.Options`
- Implements `IDisposable` since the complog reader holds file handles

### 6. Config file placement of complog path

The `complog` path in `twohash.config.json` resolves relative to the config file (same as `project` and `cacheDir`). This makes it natural to reference a complog in the repo: `{"complog": "./artifacts/build.complog"}`.

## Risks / Trade-offs

**[Risk] Large dependency size** → `Basic.CompilerLog.Util` may add significant size to the TwoHash.Core package. Mitigation: Measure the impact. If too large, consider making it a separate `TwoHash.Complog` package that users opt into. For now, include it directly since the CLI is already a large .NET tool.

**[Risk] complog library API instability** → The complog library is maintained by Jared Parsons but is not a stable/versioned product. API may change. Mitigation: Pin to a specific version, wrap usage behind `ComplogResolver` to isolate the dependency. Monitor releases.

**[Risk] Stale complog files** → If the project changes but the complog isn't regenerated, twohash gives results based on old references. Mitigation: Document that complogs are snapshots. The result cache key includes the complog's last-write-time, so regenerating the complog invalidates the cache. A future enhancement could compare complog creation time to source file mtime and warn.

**[Risk] complog reader holds file handles** → The complog reader may keep the file open during processing. Mitigation: `ComplogResolver` implements `IDisposable`. `TwohashProcessor` disposes it after extracting references. For `verify` command (multiple files), the resolver is opened once and shared across files, disposed at the end.

**[Trade-off] No framework/package override with complog** → When using complog, `--framework` and `#:package` directives are ignored for resolution. The complog's compilation state is authoritative. This simplifies the model but means users can't "patch" a complog with additional references. This is acceptable — if you need different references, rebuild the complog.
