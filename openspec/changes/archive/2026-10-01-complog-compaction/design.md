## Context

Glosharp consumes `.complog` files through `ComplogResolver` in `GloSharp.Core`. The resolver extracts only `MetadataReference[]`, `CSharpCompilationOptions`, `CSharpParseOptions`, and a target framework string — it never runs analyzers (it passes `BasicAnalyzerKind.None`) and never uses the original project source trees (glosharp swaps in its own). That means most of a raw complog's bytes are payload that `ComplogResolver` pays for on disk but never reads:

- IL bodies, private members, embedded PDBs, and resources inside each referenced assembly
- Analyzer and generator DLLs
- Duplicate copies of the same reference across per-project compilations
- Original project source trees
- Generator-produced source text (glosharp never sees these — it compiles the user's snippet, not the original project's code)

A typical ASP.NET complog is in the tens of MB. Users want to commit a compilation context alongside their docs so CI and contributors can render snippets without NuGet restore. Tens of MB feels heavy for git; single-digit MB does not.

Experiment results on two fixtures (full data in `.context/complog-experiments/`):

| Pipeline stage                                           | BclOnly (raw 7.1 MB) | AspNet (raw 15.6 MB) |
|---------------------------------------------------------|---------------------:|---------------------:|
| Recompress raw complog with zstd-19 --long              |   5.60 MB (−21%)     |  11.9 MB (−23%)      |
| Drop analyzers + sources, keep raw refs + zstd          |       —              |  4.09 MB (−74%)      |
| Refasm refs + zstd                                       |   870 KB (−88%)      |  2.18 MB (−86%)      |
| Refasm refs + zstd + `--patch-from` BCL baseline         |   1.5 KB (−99.98%)   |  1.22 MB (−92%)      |
| Raw (unrefasmed) refs + zstd + `--patch-from`            |       —              |  3.60 MB (−77%)      |

Key takeaways driving the design:

- **Refasmer is the single biggest win** on NuGet-heavy references. Spot checks: `Microsoft.EntityFrameworkCore.dll` 2.68 MB → 922 KB (−66%), `FluentAssertions.dll` 740 KB → 150 KB (−80%). BCL refs are already reference-shaped and barely move (e.g. `System.Runtime.dll` 883 KB → 832 KB).
- **Analyzers are heavy**. Two analyzer DLLs dominate 70% of the analyzer section (`Microsoft.CodeAnalysis.NetAnalyzers.dll` 6.2 MB, `Microsoft.CodeAnalysis.Razor.Compiler.dll` 4.5 MB). Dropping them is a free 15 MB win on AspNet before any other step.
- **Zstd `--long=27` does intra-archive dedupe for free**. A 128 KB trained dictionary produced essentially no improvement over zstd alone (2.10 MB vs 2.18 MB, noise). Trained dictionaries are ruled out.
- **`--patch-from` works and roundtrips bit-identically**, but the win for realistic projects is ~1 MB absolute (2.18 MB → 1.22 MB on AspNet). It also only works if both baseline and artifact go through Refasmer — shipped BCL refs and refasmer output differ by enough bytes that patch-from collapses without refasmer (3.60 MB instead of 1.22 MB). The complexity of shipping and versioning baselines across TFMs outweighs this win for v1.
- **Compacted complogs should stop pretending to be complogs**. The old proposal kept the output as a valid `.complog` readable by `Basic.CompilerLog.Util`. After stripping IL, analyzers, and sources that contract is a lie — a general complog consumer would crash or produce wrong results. Owning a format removes the pretense.

## Goals / Non-Goals

**Goals:**
- Define a `.glocontext` file format with a versioned header that glosharp can read via a new `GloContextResolver`
- Produce `.glocontext` files via CLI (`glosharp compact-complog`) and library API (`ComplogCompactor`)
- Hit single-digit MB for a typical ASP.NET + EF + NuGet project on first release
- Reserve header slots so `zstd --patch-from`-style baselines can be added later without a format version bump
- Let `process`/`verify`/`render` transparently accept `.glocontext` files via the existing `--complog` option

**Non-Goals:**
- Shipping pre-trained `--patch-from` baselines in v1 — reserved for v2 after measuring real-world benefit
- Trained zstd dictionaries — experiment showed no benefit
- Keeping `.complog` compatibility in the output format
- Running compaction in-process during rendering — this is an offline tool; rendering consumes already-produced `.glocontext` files
- Forking or patching `Basic.CompilerLog.Util` or `JetBrains.Refasmer`
- Exposing compaction through the Node bridge in this change (future enhancement)

## Decisions

### 1. New format, not a compacted complog

**Options:**
- (a) Keep output as a valid `.complog`, readable by `Basic.CompilerLog.Util`
- (b) Define a new format `.glocontext`

**Decision: (b)**

After stripping analyzers, original sources, generated sources, and post-Refasmer rewriting, the file no longer satisfies the implicit contract of a complog. A general `Basic.CompilerLog.Util` consumer expects to be able to rerun generators, inspect original source, run analyzers, and get faithful diagnostics — we've removed all of that. Pretending is worse than not pretending. Owning the format also lets us pick our own container (zstd), our own manifest schema, and our own header with versioning + reserved slots for future features.

### 2. Zstd container with `--long=27`-equivalent mode

**Options:**
- (a) Stay on zip/deflate (like complog)
- (b) Zstd

**Decision: (b) — zstd**

Measurements: zstd-19 with `--long=27` produced 2.18 MB vs 2.79 MB for plain zstd-3 on the AspNet fixture (refasm'd refs), and gzip-9 landed at 2.89 MB. Long-range mode is particularly useful for us because a multi-project context can still have near-duplicate reference bytes across compilations after MVID dedupe (e.g. differently-cased metadata, slightly different attribute ordering); long-range matching mops those up.

Runtime dependency: `ZstdSharp.Port` 0.8.7 — a managed port of zstd with `ZstdCompressor`/`ZstdDecompressor` APIs and `--long` window support. When .NET 11 ships with zstd in `System.IO.Compression` (already merged on main), we swap the implementation behind `GloContextWriter`/`GloContextReader` with no format change. Until then, `ZstdSharp.Port` is the runtime dependency.

### 3. Refasmer mandatory by default

**Options:**
- (a) Refasmer opt-in
- (b) Refasmer opt-out (debug flag only)
- (c) Refasmer mandatory

**Decision: (b) — opt-out via `--no-refasm`, debug-only**

The experiment showed that without Refasmer the output is far larger (3.60 MB vs 1.22 MB with patch-from; 4.09 MB vs 2.18 MB without). Refasmer is also needed for determinism — refasmer-produced bytes are stable across runs of the same version, so dedupe and future patch-from both rely on it. `--no-refasm` remains useful for debugging edge cases (e.g. a refasmer bug surfaces) but is documented as debug-only in help text.

An assembly already marked `ReferenceAssemblyAttribute` is passed through unchanged (already minimal — refasmer usually makes no meaningful difference and can break odd cases).

### 4. Strip analyzers, original sources, and generator output unconditionally (with debug opt-outs)

`ComplogResolver` passes `BasicAnalyzerKind.None` today, so analyzers never run. Stripping them from the archive is just removing bytes we never read. Original sources are never loaded by `ComplogResolver`. Generator-produced text is only useful if glosharp were compiling the original project's tree — it compiles the user's snippet in a fresh compilation instead, so generator output is dead weight too.

`--keep-analyzers` / `--keep-sources` / `--keep-generated` remain as debug flags for edge cases (e.g. someone wants analyzer DLLs in the artifact for a different tool, or a future glosharp feature wants to surface generator-produced types).

### 5. Deduplicate references by content hash

Multi-project complogs store the same reference per compilation. After Refasmer the post-rewrite bytes are deterministic, so a SHA-256 over each rewritten assembly's bytes is a stable content key. The manifest records per-compilation reference lists that point at shared blob entries. `ReferencesBefore` counts the sum of per-compilation lists; `ReferencesAfter` counts unique blobs.

### 6. File format v1

Layout, all little-endian:

| Offset | Size | Name            | Description                                                             |
|-------:|-----:|-----------------|-------------------------------------------------------------------------|
|      0 |    6 | Magic           | ASCII `GLOCTX`                                                          |
|      6 |    1 | FormatVersion   | `0x01` for v1                                                           |
|      7 |    1 | FlagsByte       | Reserved; readers MUST reject non-zero unknown flags                    |
|      8 |    8 | BaselineId      | v1: always `0x00…00`. v2: 64-bit id identifying a shared baseline      |
|     16 |    4 | BaselineVersion | v1: always `0`. v2: monotonic version of the baseline                   |
|     20 |    8 | Reserved        | Must be zero in v1                                                      |
|     28 |    — | Payload         | Zstd-compressed stream                                                  |

Payload, after zstd decompress, is a TAR stream containing:
- `manifest.json` — schema below
- `refs/<sha256-hex>.dll` — one entry per unique reference blob

Manifest schema (JSON, UTF-8):

```jsonc
{
  "version": 1,
  "compilations": [
    {
      "projectName": "AspNet.csproj",
      "targetFramework": "net11.0",
      "language": "csharp",
      "compilationOptions": {
        "outputKind": "ConsoleApplication",
        "nullableContext": "Enable",
        "platform": "AnyCpu",
        "allowUnsafe": false,
        "specificDiagnosticOptions": { /* ... */ }
        // … full set needed to reconstruct CSharpCompilationOptions
      },
      "parseOptions": {
        "languageVersion": "Latest",
        "documentationMode": "Parse",
        "preprocessorSymbols": ["TRACE", "RELEASE", "NET", "NET11_0"],
        "features": { /* ... */ }
      },
      "references": [
        { "blob": "<sha256>", "display": "System.Runtime.dll", "aliases": [] },
        // …
      ]
    }
  ]
}
```

`blob` points at `refs/<sha256>.dll` in the tar. Multiple compilations' entries may share a blob.

Choice of hash: SHA-256 rather than MVID. MVID is stored inside a PE and survives Refasmer, but it's 128 bits and collisions across unrelated assemblies are possible in theory. SHA-256 over the post-Refasmer bytes is just as stable and definitively non-colliding.

### 7. Consuming `.glocontext`: a sibling resolver

New `GloContextResolver` class in `GloSharp.Core`:

```csharp
public sealed class GloContextResolver : IDisposable
{
    public static GloContextResolver Open(string path);
    public GloContextResolutionResult Resolve(string? projectName = null);
}
```

Shape mirrors `ComplogResolver` so `GloSharpProcessor` can treat them symmetrically. The existing `--complog` CLI option and `ComplogPath`/`ComplogProject` properties on `GloSharpProcessorOptions` are extended: they sniff magic bytes at open time and dispatch to the right resolver. Users don't have to care which format they have.

### 8. Baseline slots reserved but unimplemented

Header includes `BaselineId` and `BaselineVersion`. v1 always writes zero; v1 readers MUST reject non-zero values (they don't know how to apply baselines). v2 will add baseline negotiation: the CLI will ship (or download-and-cache) baseline blobs; the writer uses `zstd --patch-from` mode against the baseline and records the id/version; the reader looks up the baseline, feeds it to `zstd --patch-from` decompression. The experiment numbers (BclOnly → 1.5 KB, AspNet → 1.22 MB) demonstrate the ceiling but don't justify the lifecycle cost yet.

Writing the slots now costs 12 bytes of header. Not writing them would make v2 a format bump.

### 9. `--complog` CLI flag auto-detects format

Rather than adding a parallel `--glocontext` flag, the existing `--complog` option on `process`/`verify`/`render` opens the file, reads the first 6 bytes, and dispatches:
- `GLOCTX` → `GloContextResolver`
- `PE\0\0` / zip magic → `ComplogResolver`
- anything else → error with a clear "not a recognized compilation context" message

This keeps the user-facing surface simple: there's one "compilation context" concept; the format is an implementation detail.

### 10. Library API shape

```csharp
public sealed class ComplogCompactor
{
    public static ComplogCompactionResult Compact(
        string inputPath,
        string outputPath,
        ComplogCompactionOptions options);
}

public sealed record ComplogCompactionOptions
{
    public bool RewriteReferences { get; init; } = true;
    public bool DropAnalyzers { get; init; } = true;
    public bool DropOriginalSources { get; init; } = true;
    public bool DropGeneratedSources { get; init; } = true;
    public int ZstdLevel { get; init; } = 19;
    public int ZstdWindowLog { get; init; } = 27;
}

public sealed record ComplogCompactionResult
{
    public long InputSizeBytes { get; init; }
    public long OutputSizeBytes { get; init; }
    public int ReferencesBefore { get; init; }
    public int ReferencesAfter { get; init; }
    public int AnalyzersDropped { get; init; }
    public int OriginalSourcesDropped { get; init; }
    public int GeneratedSourcesDropped { get; init; }
}
```

### 11. CLI surface

```
glosharp compact-complog <input.complog> -o <output.glocontext>
                         [--keep-analyzers]
                         [--keep-sources]
                         [--keep-generated]
                         [--no-refasm]
                         [--zstd-level <n>]
                         [--quiet]
```

Summary on stderr (suppressed by `--quiet`):
```
compact-complog: AspNet.complog → AspNet.glocontext
  references:    321 → 187 (dedupe by content hash)
  rewritten:     174 (refasm)
  analyzers:     dropped (7 DLLs, 14.8 MB)
  sources:       dropped (126 entries, 2.3 MB)
  generated:     dropped (12 entries, 340 KB)
  size:          15.6 MB → 2.18 MB (86% reduction)
```

Exit 0 on success; non-zero on input error, output error, Refasmer failure, or unsupported complog shape.

## Risks / Trade-offs

**[Risk] Refasmer edge cases** → Some assemblies rely on quirks Roslyn's symbol APIs don't need (e.g. specific custom-attribute placement) that Refasmer normalizes. **Mitigation:** Round-trip test in the suite — compact a fixture and assert that symbols, hovers, and completions extracted via glosharp match the uncompacted version. Pin Refasmer version. `--no-refasm` escape hatch for debugging.

**[Risk] Format ossification** → Once `.glocontext` is out in the wild, format changes are real work. **Mitigation:** FormatVersion byte lets us ship v2 cleanly; reserved baseline slots and FlagsByte give us room to add features without a version bump. Readers reject unknown flags so old readers on new writers fail loudly rather than silently corrupting.

**[Risk] Two zstd implementations over time** → `ZstdSharp.Port` now, `System.IO.Compression` on .NET 11. **Mitigation:** Wrap both behind a small internal `IZstdCodec` abstraction in `GloContextWriter`/`GloContextReader` so the switch is a one-file change. Both produce standard zstd streams, so artifacts don't care which side wrote them.

**[Risk] No `.complog` fallback in the compact artifact** → Users who want a true complog keep the raw one. **Mitigation:** Documentation. The expected workflow is: CI generates a raw complog, the same step compacts it to `.glocontext`, raw complog is discarded, `.glocontext` is committed. Users who want both keep both.

**[Risk] Losing analyzer diagnostics in rendered docs** → Analyzer-produced warnings don't reach glosharp's diagnostic extraction. **Mitigation:** Already the status quo — `ComplogResolver` passes `BasicAnalyzerKind.None`, so compaction isn't making this worse, just making it explicit. Summary surfaces the drop.

**[Trade-off] Compaction is lossy by default** → A `.glocontext` cannot reconstruct the original project's sources, analyzers, or generator output. Deliberate: the whole point is that glosharp's consumer doesn't need any of it. A user who wants a full-fidelity artifact keeps the raw complog.

**[Trade-off] 12 reserved header bytes for a feature we might not ship** → Cheap insurance. 12 bytes is lost in zstd compression noise anyway.

## Open Questions

- Do we want `.glocontext` writing to be deterministic byte-for-byte across invocations? Refasmer is deterministic, SHA-256 hashes sort stably, zstd is deterministic at a fixed level. The only non-determinism would be manifest JSON ordering — we can enforce sorted keys. Recommendation: yes, make it deterministic; deterministic outputs are friendlier to git (no spurious diffs when re-generating a `.glocontext` that hasn't meaningfully changed).
- Should the CLI emit both a `.complog` (unchanged) and a `.glocontext` by default, or overwrite? Current plan: the CLI is `compact-complog` — input is a `.complog`, output is a `.glocontext` at a separate path. No implicit overwrite. Users decide what to keep.
