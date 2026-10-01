## 1. Dependencies & Project Setup

- [x] 1.1 Add `JetBrains.Refasmer` NuGet package reference to `GloSharp.Core.csproj`, pinned to a specific version
- [x] 1.2 Add `ZstdSharp.Port` 0.8.7 NuGet package reference to `GloSharp.Core.csproj`
- [x] 1.3 Verify the project restores and builds cleanly

## 2. GloContext Format Primitives

- [x] 2.1 Add a `GloContextHeader` struct / constants (magic `GLOCTX`, version byte `0x01`, flags, baseline id/version slots, reserved) with read/write helpers in `GloSharp.Core`
- [x] 2.2 Add an internal `IZstdCodec` abstraction with a `ZstdSharpCodec` implementation using `ZstdSharp.Port` so the future swap to `System.IO.Compression` zstd is a one-file change
- [x] 2.3 Define the manifest schema types (compilation, compilation options, parse options, reference entry) as records with sorted-key JSON serialization for deterministic output

## 3. ComplogCompactor Library API

- [x] 3.1 Add `ComplogCompactionOptions` record with `RewriteReferences`, `DropAnalyzers`, `DropOriginalSources`, `DropGeneratedSources`, `ZstdLevel = 19`, `ZstdWindowLog = 27` defaults (all `true` for the drop/rewrite flags)
- [x] 3.2 Add `ComplogCompactionResult` record with input/output sizes, reference counts before/after, refasm count, and drop counts for analyzers, original sources, and generated sources
- [x] 3.3 Add `ComplogCompactor` class with a static `Compact(string inputPath, string outputPath, ComplogCompactionOptions options)` method returning `ComplogCompactionResult`
- [x] 3.4 Input validation: `FileNotFoundException` when input missing, `IOException` when output directory unwritable, atomic write (temp file + rename), cleanup on failure

## 4. Reading the Input Complog

- [x] 4.1 Open the input with `CompilerCallReaderUtil.Create(inputPath, BasicAnalyzerKind.None)`
- [x] 4.2 Enumerate compiler calls and collect: references, analyzer/generator DLL paths, original source entries, generated-source entries, compilation options, parse options, target framework
- [x] 4.3 Detect references that are already marked `ReferenceAssemblyAttribute` and mark them as Refasmer-skip

## 5. Reference-Assembly Rewriting

- [x] 5.1 For each reference (when `RewriteReferences` is true and the reference is not already a reference assembly), run `JetBrains.Refasmer` with `omitNonApiMembers = true` into an in-memory byte array
- [x] 5.2 Surface Refasmer failures with the offending assembly name via a clear exception; do not swallow
- [x] 5.3 Compute SHA-256 over each final reference byte array to produce a content hash

## 6. Deduplication and Manifest Assembly

- [x] 6.1 Build a `Dictionary<hash, blobEntry>` so identical post-Refasmer references share one stored blob
- [x] 6.2 Build the manifest: one entry per compilation, each with compilation options, parse options, target framework, and an ordered reference list pointing at blob hashes
- [x] 6.3 Track `ReferencesBefore` (sum of per-compilation reference counts) and `ReferencesAfter` (unique blob count)

## 7. Writing the .glocontext File

- [x] 7.1 Write the fixed header (magic + version + flags + reserved baseline slots) to the output stream
- [x] 7.2 Build the tar payload in memory or a streamed writer: `manifest.json` first, then `refs/<hash>.dll` entries sorted by hash for determinism
- [x] 7.3 Feed the tar stream through zstd (via `IZstdCodec`) at the configured level and window log; append the compressed bytes after the header
- [x] 7.4 Write to a temp file in the target directory and atomically rename over the destination on success; delete the temp file on any failure

## 8. GloContextResolver

- [x] 8.1 Add a `GloContextResolver` class in `GloSharp.Core` with `Open(string path)` factory and `Resolve(string? projectName = null)` method
- [x] 8.2 Read and validate the header: magic, version (reject unknown), flags (reject unknown bits), baseline id/version (reject non-zero in v1)
- [x] 8.3 Decompress the zstd payload, read the tar, parse `manifest.json`, load blob bytes into `MetadataReference` instances preserving order and aliases
- [x] 8.4 Reconstruct `CSharpCompilationOptions` and `CSharpParseOptions` from the manifest
- [x] 8.5 Implement `IDisposable` and release any retained streams/handles on dispose

## 9. Integration with Existing --complog Flag

- [x] 9.1 Change the `--complog` open path in the CLI to sniff the first bytes: `GLOCTX` → `GloContextResolver`, `PK\x03\x04` → `ComplogResolver`, other → error
- [x] 9.2 Add a shared internal factory in `GloSharp.Core` that takes a path and returns a common `ICompilationContextResolver` interface; keep both resolvers behind it
- [x] 9.3 Update `GloSharpProcessor.ProcessAsync` and the verify/render paths to use the factory

## 10. CLI Integration

- [x] 10.1 Add a `compact-complog` subcommand to `GloSharp.Cli` with a required positional `input` and required `-o` / `--output`
- [x] 10.2 Add `--keep-analyzers`, `--keep-sources`, `--keep-generated`, `--no-refasm`, `--zstd-level <n>`, `--quiet` flags; mark the first four as debug options in help text
- [x] 10.3 Emit the compaction summary to stderr from `ComplogCompactionResult` (suppressed by `--quiet`); keep stdout empty on success
- [x] 10.4 Map exceptions to non-zero exit codes: missing input, unwritable output, Refasmer failure, unrecognized complog shape

## 11. Tests

- [x] 11.1 Commit a small multi-project source fixture under `tests/GloSharp.Tests/fixtures/complogs/MultiProject/`; `ComplogFixture.GetOrBuildMultiProjectComplog` lazily generates the `.complog` from a `dotnet build -bl` + `CompilerLogUtil.TryConvertBinaryLog` and caches it in the test `bin/` directory
- [x] 11.2 Round-trip test: compact a fixture, open with `GloContextResolver`, assert references resolve expected public types, compilation options round-trip, and target framework matches
- [x] 11.3 Symbol/hover/completion parity test: process the same snippet against the raw complog and the compacted `.glocontext`, assert JSON outputs match for public-API behavior
- [x] 11.4 Size-reduction test: assert the AspNet fixture compacts to under 3 MB and BclOnly to under 1 MB
- [x] 11.5 Determinism test: compact the same input twice, assert outputs are byte-identical
- [x] 11.6 Flag tests: `--keep-analyzers`, `--keep-sources`, `--keep-generated`, `--no-refasm` each alter the payload as expected
- [x] 11.7 Dedupe test: multi-project complog yields `ReferencesAfter < ReferencesBefore` in the result
- [x] 11.8 Header validation tests in `GloContextResolver`: bad magic, unknown format version, unknown flags, non-zero baseline id in v1 each throw with clear messages
- [x] 11.9 `--complog` routing tests: a `.glocontext` is routed to `GloContextResolver`, a `.complog` to `ComplogResolver`, garbage errors cleanly
- [x] 11.10 Error tests: missing input, unwritable output, simulated Refasmer failure each exit non-zero with no partial output left behind

## 12. Documentation

- [x] 12.1 Add a README section describing `compact-complog`, the `.glocontext` format at a high level, what is dropped by default, and the commit-to-git use case
- [x] 12.2 Document the `ZstdSharp.Port` dependency with a note that it will be replaced by `System.IO.Compression` when .NET 11 ships GA
- [x] 12.3 Note that the format reserves baseline slots for a future v2 feature; v1 readers MUST reject non-zero baseline id to protect forward-compat
