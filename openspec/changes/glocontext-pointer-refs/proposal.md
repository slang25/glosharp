## Why

Experiments (`.context/complog-experiments/RESULTS.md`) show that ~95% of a `.glocontext`'s bytes are framework reference assemblies that are publicly and permanently available as NuGet targeting packs (`microsoft.netcore.app.ref`, `microsoft.aspnetcore.app.ref`). Replacing those embedded blobs with verifiable pointers shrinks a typical ASP.NET context from 2.89 MB to ~450 KB and a BCL-only context to ~8 KB — small enough to commit to git without thought. The experiments also uncovered two blocking bugs in the current compactor: Refasmer is invoked with `omitNonApiMembers: false` (contradicting the complog-compaction spec) which hard-fails on EF Core, FluentAssertions, FluentValidation, and CsvHelper; and `IsReferenceAssembly` misses `System.Runtime.dll` because that assembly declares `ReferenceAssemblyAttribute` via MethodDef rather than MemberReference.

## What Changes

- **BREAKING (format):** `.glocontext` format version bumps to v2. References in the manifest may now be *pointers* (`{pack, path, sha256}` into a targeting pack) instead of embedded blobs. v1 files remain readable; v2 files are rejected by v1 readers via the existing version check.
- **Canonicalization:** at compact time, each framework reference is matched against the NuGet-channel targeting pack (by MVID, falling back to assembly file name) and replaced with the pack's canonical bytes' pointer. This makes producer and consumer byte-exact regardless of which distribution channel (installed SDK vs nuget.org) the original build used.
- **Pack acquisition:** a new resolver-side component locates targeting packs — NuGet global packages folder first, then a glosharp-managed cache, then download from nuget.org — and verifies pointer hashes before use.
- **Self-contained fallback:** `--self-contained` CLI flag (and `ComplogCompactionOptions.SelfContained`) keeps the current embed-everything behavior, emitting v1.
- **Bug fixes:** Refasmer invoked with `omitNonApiMembers: true` (as the complog-compaction spec already required); `IsReferenceAssembly` also recognizes the MethodDef-declared attribute.

## Capabilities

### New Capabilities
- `glocontext-pointer-refs`: v2 manifest schema with pointer references, canonicalization rules at compact time, and pointer resolution + hash verification at read time.
- `reference-pack-acquisition`: locating, caching, downloading, and verifying NuGet targeting packs used by pointer resolution (producer and consumer side).

### Modified Capabilities
- `complog-compaction`: compaction now canonicalizes framework refs to pointers by default (self-contained becomes opt-in); Refasmer invocation fixed to `omitNonApiMembers: true`; reference-assembly detection covers MethodDef-declared attributes.
- `glocontext-resolution`: resolver understands format v2, resolves pointers via pack acquisition, and reports actionable errors when packs are unavailable (offline) or hashes mismatch.
- `cli-tool`: `compact-complog` gains `--self-contained`; summary output reports pointer/embedded counts and the packs required by the artifact.

## Impact

- `src/GloSharp.Core`: `ComplogCompactor`, `GloContextFormat` (version byte), `GloContextManifest` (schema v2), `GloContextResolver`, new `ReferencePackResolver` (acquisition), `ZstdCodec` untouched.
- `src/GloSharp.Cli`: `compact-complog` flags and summary.
- Tests: new unit/integration tests using local pack fixtures (no network in CI); existing round-trip, determinism, and parity tests extended to v2.
- Consumers: first `open()` of a v2 file on a machine without the packs downloads ~7 MB per pack (then cached). Committed artifacts shrink ~6×.
- No new runtime dependencies (uses `HttpClient` + existing `System.Formats.Tar`/zip APIs).
