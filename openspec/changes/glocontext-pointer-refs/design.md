## Context

`.glocontext` v1 (change `complog-compaction`) embeds every reference assembly as a refasmed blob. Experiments (`.context/complog-experiments/RESULTS.md`) established:

- Framework refs (targeting-pack assemblies) are ~95% of the payload and are publicly, permanently, hash-verifiably available as NuGet packages (`microsoft.netcore.app.ref`, `microsoft.aspnetcore.app.ref`), ~7.2 MB nupkg each.
- The bytes the compiler consumed (installed SDK's pack) are **not** byte-identical to the same version on nuget.org: every file differs in PE timestamp/checksum + signature, and a subset (facades built by GenFacades, plus ~25 AspNetCore assemblies) are different builds entirely — different MVID at the same package version. 223/321 refs in the AspNet fixture match nuget.org by MVID; 84 only by file name; 14 are NuGet library refs with no framework source.
- Therefore pointers must record the *canonical* (nuget.org-channel) bytes, and the compactor must swap ("canonicalize") its embedded bytes to those before writing, or hashes can never be verified on the consumer side.
- Measured sizes with canonicalized pointers: BclOnly 1.14 MB → 7.6 KB, AspNet 2.89 MB → 461 KB.
- Two production bugs block real-world compaction today: `RefasmBytes` passes `omitNonApiMembers: false` (spec said `true`), which throws `UnknownTypeInSignature` on EF Core / FluentAssertions / FluentValidation / CsvHelper; and `IsReferenceAssembly` misses `System.Runtime.dll` (declares the attribute via MethodDef, not MemberReference).

## Goals / Non-Goals

**Goals:**
- `.glocontext` v2: framework refs as `{pack, path, sha256}` pointers, canonicalized to nuget.org-channel bytes; ~6× smaller artifacts.
- Deterministic output preserved (byte-identical recompaction).
- Consumer-side pack acquisition with a no-network happy path (NuGet global packages folder), a glosharp cache, and nuget.org download as last resort; sha256 verification of every pointed file.
- Graceful degradation: producer falls back to embedding (with a warning) when canonical packs are unavailable; `--self-contained` opts out of pointers entirely and writes v1.
- Fix the two compactor bugs independently of the pointer feature.

**Non-Goals:**
- Pointer-izing NuGet library refs (would require consumer-side Refasmer determinism contract; future work — measured ceiling is a ~10 KB pure-manifest artifact).
- zstd `--patch-from` baselines (equivalent size to pointers, strictly more machinery; header slots stay reserved).
- Supporting non-nuget.org feeds for pack download (packs are Microsoft-published; private-feed users can pre-populate the GPF or cache).
- VB compilations (existing v1 limitation, unchanged).

## Decisions

### 1. Pointer eligibility: origin-path gating, not name matching

A complog reference is considered a *framework ref candidate* only when its origin file path (from the compiler call's `/reference:` arguments / `ReferenceData` path) sits under a recognized targeting-pack layout:

- `<dotnet root>/packs/<PackId>/<Version>/ref/<tfm>/...` (installed SDK channel)
- `<GPF>/<packid>/<version>/ref/<tfm>/...` (NuGet channel)

This yields the exact `(packId, version)` pairs the build used and prevents a malicious/coincidental package DLL named `System.Runtime.dll` from being silently swapped for framework bytes. Everything else follows the v1 refasm+embed path.

### 2. Canonicalization matching: raw hash, then MVID, then file name + assembly version

Against the canonical pack's `ref/<tfm>/*.dll` files (acquired from the NuGet channel):

1. raw SHA-256 equality (producer built against NuGet-channel packs — bytes already canonical),
2. MVID equality (same build, different signing/timestamps — covers ~70%),
3. file name + assembly `Version` equality (facades and channel-rebuilt assemblies; semantically same release).

Matched → pointer entry `{pack, path, sha256(canonical bytes)}`; canonical bytes never enter the tar. Unmatched pack refs (shouldn't happen; e.g. version skew mid-release) → warn + embed. The name-fallback is the only semantic substitution; the existing symbol-parity test suite gates it (extended to run in pointer mode).

### 3. Format: header version 0x02 when pointers present; v1 for self-contained

- Manifest gains `"version": 2`, a top-level `"packs": [{"id", "version", "sha256"}]` array (sorted), and reference entries become a union: blob refs keep `{"blob", ...}`; pointer refs are `{"pack": <index>, "path", ...}` (exactly one of `blob`/`pack` present; readers reject entries with both/neither).
- **Whole-pack collapse**: the SDK passes the compiler every DLL in the pack's ref directory, so an explicit path list carries no information. When a compilation's pointers into a pack are exactly its `ref/<tfm>/` direct children with default properties, they collapse to one `{"packAll": i, "tfm": "..."}` entry; the reader re-expands from the verified pack contents (the pack content hash pins the file set, so expansion cannot drift). All-or-explicit — no `except` lists. Reference order becomes sorted-by-path for expanded refs; Roslyn only cares about order for duplicate identities, which cannot occur within a pack, and the symbol-parity suite gates it. BclOnly: 2.5 KB → 766 bytes.
- Verification is **per pack, not per file**: `packs[].sha256` is a content hash over the pack's `ref/**/*.dll` (per file sorted by relative path: UTF-8 path + 0x00 + file SHA-256). Measured on the BclOnly fixture, 167 per-file sha256 fields were 73% of the compressed manifest (8.7 KB → 2.3 KB when dropped) — pure hash entropy that compression cannot touch, and redundant since every pointer targets the same verified pack. One hash per pack keeps the identical trust model (the resolver verifies exactly the bytes it will read) at ~32 bytes of entropy per pack instead of per file.
- Writer emits header `FormatVersion 0x02` iff any pointer exists; `--self-contained` output is exactly today's v1 (older readers keep working on it).
- v2 readers accept v1 and v2. v1 readers already reject v2 via the existing version check — loud, actionable failure.
- Baseline header slots remain zero/reserved (patch-from stays a possible v3).

### 4. Pack acquisition: ordered source chain, injectable for tests

New `ReferencePackResolver` in `GloSharp.Core` used by both compactor (canonicalization) and resolver (pointer resolution):

1. **NuGet global packages folder** — `NUGET_PACKAGES` env var, else `~/.nuget/packages`. Populated by normal restores; bytes are canonical NuGet-channel by construction. No network.
2. **glosharp cache** — `GLOSHARP_CACHE_DIR` env var, else `<LocalApplicationData>/glosharp/packs/<id>/<version>/`. Populated only by step 3.
3. **nuget.org download** — `https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg` (lowercased), extract only `ref/**/*.dll` into the glosharp cache via `System.IO.Compression.ZipArchive`. Plain `HttpClient`; no NuGet client dependency.

The chain is an ordered `IReadOnlyList<IPackSource>`; tests inject a directory-backed source, so CI never touches the network. The installed SDK `packs/` directory is deliberately **not** a source: its bytes are non-canonical (different signing/builds), so hash verification would fail.

Consumer verifies each pack's content hash once at open time (~50 ms for a full pack — same work as per-file verification since typical pointer sets cover the whole pack; no verification cache in this change) and serves pointer reads from the verified snapshot. Mismatch → `InvalidDataException` naming the pack, its location, and both hashes.

### 5. Failure modes are explicit

- Producer, canonical pack unavailable (offline, delisted): stderr warning naming the pack, refs embedded instead (output may then be v1 if no pointers remain). No flag to force-fail in this change.
- Consumer, pack unavailable: exception message lists the missing `{id, version}`, the three lookup locations, and remedies ("restore any project targeting this framework", "re-run with network", "ask the producer for --self-contained").
- Consumer, hash mismatch: hard failure (no silent fallback) — the artifact's contract is exact bytes.

### 6. Bug fixes ride along, unconditionally

- `RefasmBytes`: `new AllowPublic(omitNonApiMembers: true)` + `omitNonApiMembers: true`, matching the complog-compaction spec (task 5.1) and the resolver's actual needs (private members are never surfaced). Empirically un-blocks EF Core, FluentAssertions, FluentValidation, CsvHelper (0 failures across fixtures).
- `IsReferenceAssembly`: also recognize `ca.Constructor.Kind == MethodDefinition` (attribute defined in the same assembly — the `System.Runtime.dll` case).

## Risks / Trade-offs

- [Name-fallback substitution is semantic, not byte-level] Same-version rebuilds could theoretically differ in API. → Scope: only pack-origin refs at the exact pack version the build used; parity tests run the pointer path; facades (the bulk) are type-forwarder-only.
- [First open needs packs] Offline consumers without a restored pack fail. → GPF hit is the common case (any restore of a matching TFM populates it); clear remediation message; `--self-contained` escape hatch at produce time.
- [nuget.org outage/delisting] Targeting packs are Microsoft-published and effectively permanent; GPF/cache layers absorb transient outages.
- [CI network use in tests] Integration tests use directory-backed pack sources; only an opt-in (env-gated) test exercises real download.
- [Refasm output changes with `omitNonApiMembers: true`] Existing v1 artifacts' blob hashes won't match newly produced ones. → Determinism is per-producer-version (already the stated contract); no reader impact.

## Migration Plan

1. Land bug fixes + v2 writer/reader + acquisition in `GloSharp.Core`, CLI flag, tests.
2. Existing v1 files continue to open unchanged; no migration required.
3. Rollback = revert; v1 writing path is retained behind `--self-contained`.

## Open Questions

- Should the resolver cache verified-pack state (sha table) to skip re-hashing on every open? Deferred until profiling shows it matters.
- Windows GPF path nuances (`%UserProfile%\.nuget\packages`) — handled via `Environment.GetFolderPath(UserProfile)`; needs a Windows CI check eventually.
