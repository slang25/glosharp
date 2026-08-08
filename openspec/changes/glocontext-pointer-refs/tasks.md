## 1. Bug Fixes (independent of pointers)

- [x] 1.1 Fix `ComplogCompactor.RefasmBytes` to pass `omitNonApiMembers: true` (filter and parameter), per the complog-compaction spec
- [x] 1.2 Extend `IsReferenceAssembly` to recognize `ReferenceAssemblyAttribute` declared via `MethodDefinition` constructor (the `System.Runtime.dll` case)
- [x] 1.3 Add regression tests: refasm an assembly with private anonymous-type fields (compile a tiny test assembly in-test); MethodDef attribute detection on a synthesized assembly

## 2. Pack Acquisition

- [x] 2.1 Add `ReferencePackResolver` with `IPackSource` chain: global packages folder (NUGET_PACKAGES / ~/.nuget/packages), glosharp cache (GLOSHARP_CACHE_DIR / LocalApplicationData), nuget.org flat-container download extracting `ref/**/*.dll` with temp-dir + atomic rename
- [x] 2.2 Failure surface: exception enumerating pack id/version and every location consulted; download errors name the URL
- [x] 2.3 Unit tests with directory-backed fake sources: hit order, cache population, partial-download cleanup, failure enumeration (no network in CI)

## 3. Manifest and Format v2

- [x] 3.1 Extend manifest types: top-level `Packs` list, `ManifestReference.Pack`/`Path`/`Sha256` (blob XOR pack validation on read), manifest `version: 2`; keep sorted-key deterministic serialization
- [x] 3.2 `GloContextFormat`: accept versions 0x01 and 0x02 on read; write 0x02 only when pointers present
- [x] 3.3 Format tests: v2 header round-trip, malformed reference entries rejected, v1 files still readable

## 4. Compactor Canonicalization

- [x] 4.1 Extract per-reference origin paths from the complog and gate framework-pack candidates by pack layout (`packs/<Id>/<Ver>/ref/<tfm>` or GPF layout), yielding exact `(id, version)` pairs
- [x] 4.2 Canonicalize candidates against acquired packs: raw SHA-256, then MVID, then file name + assembly version; emit pointer entries with canonical sha256; unmatched/unavailable → warn + embed
- [x] 4.3 Add `SelfContained` option; extend `ComplogCompactionResult` with pointer count and packs list
- [x] 4.4 Determinism: byte-identical recompaction with pointers; v1 output when zero pointers or self-contained

## 5. Resolver v2

- [x] 5.1 `GloContextResolver`: parse v2 manifests, acquire packs via `ReferencePackResolver`, verify each pointed file's sha256, materialize references preserving order/aliases/embedInteropTypes
- [x] 5.2 Error paths: hash mismatch names file/pack/hashes; missing pack error lists searched locations and remedies
- [x] 5.3 Tests: v2 round-trip via injected directory pack source; mixed blob+pointer ordering; hash-mismatch and missing-pack errors; v1 regression

## 6. CLI

- [x] 6.1 Add `--self-contained` flag to `compact-complog`; wire `SelfContained`
- [x] 6.2 Extend summary: pointer count, `<id>/<version>` pack lines, warnings for unavailable packs
- [x] 6.3 CLI tests for the new flag and summary lines — covered at the options level (`SelfContained` asserted in `Compact_MultiProject_PointerMode_ProducesSmallerV2File`), matching the repo's existing pattern of not spawning the CLI in tests; flag parsing and summary output verified manually

## 7. Integration & Parity

- [x] 7.1 Extend `ComplogFixture`-based round-trip and symbol-parity tests to the pointer path (packs from GPF or injected source; skip cleanly when unavailable)
- [x] 7.2 Size assertion: MultiProject fixture compacts to under 100 KB with pointers when packs are available
- [x] 7.3 Run full test suite (`dotnet run --project tests/GloSharp.Tests/`)

## 8. Pack-level content hash (manifest slimming)

- [x] 8.1 Add `PackContentHasher` (SHA-256 over sorted ref/**/*.dll: path + 0x00 + file hash); share between compactor and resolver
- [x] 8.2 Move verification from per-file `sha256` on pointer entries to `packs[].sha256`; pointers become `{pack, path}` (BclOnly 8.9 KB → 2.5 KB)
- [x] 8.3 Resolver verifies each pack once and serves pointed files from the verified snapshot; reject packs without a valid hash
- [x] 8.4 Tests: hasher determinism/order-independence/tamper detection, pack hash mismatch, missing pointed file, hashless pack rejection

## 9. Documentation

- [x] 9.1 README: pointer mode, first-open pack acquisition, `--self-contained`, offline guidance
- [x] 9.2 Update format description: v2 layout, pointer entry schema, pack acquisition chain
