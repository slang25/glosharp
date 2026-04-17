## Why

Raw `.complog` files from typical .NET projects weigh in at tens of megabytes — a `dotnet new webapi` with EF Core, FluentAssertions, and AutoMapper produces a 15.6 MB complog; a bare `dotnet new console` is already 7.1 MB. Most of that payload is dead weight for glosharp, which only needs public API metadata for symbol, hover, completion, and diagnostic extraction. Measurements on these fixtures (captured in `.context/complog-experiments/`) show that dropping analyzers, dropping original/generated sources, running references through Refasmer, and recompressing with zstd in long-range mode brings the 15.6 MB ASP.NET fixture to **2.18 MB** and the 7.1 MB console fixture to **870 KB** — small enough that committing the compilation context into a docs repo stops feeling like a big deal. CI and contributor machines can then render snippets with no NuGet restore and no .NET SDK beyond what glosharp already requires.

## What Changes

- New portable artifact format `.glocontext` designed for glosharp's specific needs — NOT a `.complog`. A separate format removes the impedance mismatch: after stripping IL, analyzers, and sources we no longer meet the implicit contract that a general `Basic.CompilerLog.Util` consumer expects, so we stop pretending to
- New `glosharp compact-complog <input.complog> -o <output.glocontext>` CLI command that reads a complog and writes a `.glocontext`
- New `ComplogCompactor` class in `GloSharp.Core` exposing `Compact(inputPath, outputPath, ComplogCompactionOptions)` so programmatic callers can compact during a docs build
- New `GloContextResolver` class in `GloSharp.Core` (sibling of `ComplogResolver`) that reads `.glocontext` files and produces the same `(MetadataReference[], CSharpCompilationOptions, CSharpParseOptions, TargetFramework)` shape
- `--complog` CLI option on `process`/`verify`/`render` extended to also accept `.glocontext` files (detected by magic bytes, not extension, so renaming still works)
- Zstd container via `ZstdSharp.Port` (0.8.7) for both compression and decompression, with `--long=27`-equivalent long-range mode enabled. Migration path: swap to `System.IO.Compression` zstd support when .NET 11 ships and it's available GA
- Refasmer rewriting of all reference assemblies is mandatory by default. `JetBrains.Refasmer` is a new NuGet dependency on `GloSharp.Core`
- Analyzer and source-generator DLL entries dropped unconditionally — `ComplogResolver` already passes `BasicAnalyzerKind.None` today, so they are already unused at runtime
- Original project source trees and generator-produced source text dropped unconditionally — glosharp compiles the user's snippet, never the original project's code
- References deduplicated by content hash across compilations so multi-project contexts store each assembly once
- File format includes a magic-byte header with a version byte and reserved slots for `baselineId` + `baselineVersion` — we do NOT ship baselines in v1 (experiment showed the win is ~1 MB in absolute terms on realistic projects, not worth the lifecycle cost yet), but the slots let a future version add `zstd --patch-from`-style baseline support without a format bump
- Opt-out debug flags (`--keep-analyzers`, `--keep-sources`, `--no-refasm`) for troubleshooting and edge cases. These are documented as debug-only; the default configuration is the recommended one
- End-of-run summary on stderr: input size, output size, reduction ratio, and counts for everything dropped

## Capabilities

### New Capabilities
- `complog-compaction`: Producing a `.glocontext` file — via CLI or library API — by reading a `.complog`, rewriting references with Refasmer, dropping analyzers/sources/generated-sources, deduplicating references by content hash, and packaging into a zstd-compressed container with a versioned header
- `glocontext-resolution`: Reading a `.glocontext` file and producing the `MetadataReference[]`, `CSharpCompilationOptions`, `CSharpParseOptions`, and target framework that `GloSharpProcessor` needs, plus auto-detecting `.glocontext` vs `.complog` from magic bytes so a single `--complog`/config field can accept either

### Modified Capabilities
- `cli-tool`: Adding the `compact-complog` subcommand with its options and summary output, and extending the existing `--complog` option on `process`/`verify`/`render` to auto-detect and accept `.glocontext` files as well

## Impact

- **Dependencies**: Two new NuGet packages on `GloSharp.Core` — `JetBrains.Refasmer` (reference-assembly rewriting) and `ZstdSharp.Port` v0.8.7 (zstd compression/decompression until .NET 11's built-in support lands)
- **CLI**: New `compact-complog` subcommand; `--complog` on existing commands transparently accepts `.glocontext` files too
- **Library API**: New `ComplogCompactor` + `ComplogCompactionOptions` + `ComplogCompactionResult` types, and a new `GloContextResolver` class in `GloSharp.Core`
- **File format**: `.glocontext` is a new format defined by this change. Magic-byte header (`GLOCTX\x01`) + version byte + reserved baseline slots + zstd-compressed payload containing a manifest and deduplicated reference blobs
- **Compatibility**: `.glocontext` files are NOT readable by `Basic.CompilerLog.Util`. This is intentional — we stop pretending a heavily stripped complog is still a complog
- **Tradeoffs (lossy by default)**: Analyzer-produced diagnostics disappear; re-running generators on the original project is no longer possible from the compact artifact (glosharp doesn't need to). The CLI summary makes these losses visible
- **Node bridge**: Unchanged in this scope — compaction is an offline step. A future change can surface it through the bridge if needed
