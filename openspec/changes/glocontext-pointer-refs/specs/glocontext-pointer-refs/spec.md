## ADDED Requirements

### Requirement: GloContext file format v2 with pointer references
A `.glocontext` whose manifest contains at least one pointer reference SHALL use format version `0x02` in the header byte 6. The v2 manifest SHALL carry `"version": 2`, a top-level `packs` array of `{id, version, sha256}` objects (sorted by id then version, ids and versions lowercase; `sha256` is a content hash over the pack's `ref/**/*.dll` files — per file sorted by forward-slash relative path ordinal: UTF-8 path, a zero byte, then the file's SHA-256), and reference entries in one of three shapes: blob references (`blob` key, as in v1), pointer references (`pack` index into `packs` and `path` relative to the pack root using forward slashes), or whole-pack references (`packAll` index into `packs` and `tfm`), which expand to every DLL that is a direct child of the pack's `ref/<tfm>/` directory, sorted by path ordinal, each with default reference properties. Pointer entries SHALL NOT carry per-file hashes — verification is per pack (per-file hash entropy dominated the compressed manifest at ~73% of its bytes). A reference entry SHALL contain exactly one of the three shapes; readers MUST reject entries matching zero or multiple shapes. Compactions that produce no pointer or whole-pack references SHALL emit format v1 unchanged.

#### Scenario: v2 header written when pointers present
- **WHEN** compaction canonicalizes at least one framework reference to a pointer
- **THEN** byte 6 of the output file is `0x02` and the manifest `version` is `2`

#### Scenario: v1 emitted for self-contained output
- **WHEN** compaction is run with `SelfContained = true` or produces zero pointers
- **THEN** the output is a valid format v1 file readable by v1 readers

#### Scenario: Malformed reference entry rejected
- **WHEN** a v2 manifest reference entry contains both `blob` and `pack`, or neither
- **THEN** the reader throws `InvalidDataException` identifying the offending entry

#### Scenario: Deterministic v2 output
- **WHEN** the same complog is compacted twice with the same options and the same available packs
- **THEN** the two output files are byte-identical

### Requirement: Framework references canonicalized to pack pointers by default
During compaction, a reference SHALL be considered a framework-pack candidate only when its origin path in the complog lies under a recognized targeting-pack layout (`packs/<PackId>/<Version>/ref/<tfm>/…` or `<global-packages>/<packid>/<version>/ref/<tfm>/…`), yielding the exact pack id and version used by the build. For each candidate, the compactor SHALL acquire the canonical NuGet-channel pack (per the reference-pack-acquisition capability) and match the reference against the pack's `ref/**/*.dll` files in this order: (1) raw SHA-256 equality, (2) MVID equality, (3) file name plus assembly version equality. A matched reference SHALL be written as a pointer (`pack` + `path` only); its bytes SHALL NOT be embedded in the tar, and the pack's manifest entry SHALL record the content hash of the canonical pack's ref DLLs. Unmatched candidates and all non-candidates SHALL follow the v1 refasm-and-embed path.

#### Scenario: Framework references become pointers
- **WHEN** a complog whose references originate from an installed SDK targeting pack is compacted with default options
- **THEN** each such reference appears in the manifest as a pointer into the corresponding NuGet pack and no `refs/<hash>.dll` entry exists for it

#### Scenario: MVID match despite signing differences
- **WHEN** the build consumed an installed-SDK pack file whose bytes differ from the NuGet-channel file only by signing/timestamp (same MVID)
- **THEN** the reference is canonicalized to a pointer at the NuGet-channel file's path, covered by the pack's recorded content hash

#### Scenario: Name-and-version fallback for channel-rebuilt facades
- **WHEN** a pack-origin reference matches no canonical file by hash or MVID but exactly one canonical file shares its file name and assembly version
- **THEN** the reference is canonicalized to a pointer to that file

#### Scenario: Non-pack references never become pointers
- **WHEN** a reference originates from a NuGet package lib folder or project output rather than a targeting pack
- **THEN** it is refasmed and embedded as a blob, even if its file name matches a pack assembly

#### Scenario: Canonical pack unavailable falls back to embedding
- **WHEN** the canonical pack for a candidate reference cannot be acquired (e.g. offline and not cached)
- **THEN** the compactor emits a warning naming the pack and embeds the affected references as blobs

### Requirement: Whole-pack collapse when coverage is exact
After canonicalization, the compactor SHALL replace a compilation's pointer references into a pack with a single `packAll` entry when — and only when — those pointers cover exactly the DLLs that are direct children of one `ref/<tfm>/` directory of the canonical pack and every one of them has no aliases and `embedInteropTypes` false. The `packAll` entry takes the position of the pack's first pointer. Any deviation (partial coverage, aliases, embed-interop, mixed tfm directories) SHALL keep the explicit pointer list. The collapse predicate and the reader's expansion SHALL share one definition of the direct-child DLL set so they cannot disagree.

#### Scenario: Implicit framework references collapse
- **WHEN** a compilation references a targeting pack's entire ref directory with default properties (the normal SDK behavior)
- **THEN** the manifest contains a single `packAll` entry for that pack and no per-file pointers into it

#### Scenario: Aliased framework reference prevents collapse
- **WHEN** any reference into the pack carries an external alias or embed-interop flag
- **THEN** the compilation keeps explicit pointer entries for that pack

#### Scenario: Expansion reproduces the reference set
- **WHEN** a `packAll` entry is resolved against the verified pack
- **THEN** the resolver materializes one reference per direct-child DLL of `ref/<tfm>/`, sorted by path, with display set to the file name

#### Scenario: Whole-pack reference to an absent tfm directory
- **WHEN** a `packAll` entry names a tfm with no DLLs in the verified pack
- **THEN** the resolver throws `InvalidDataException` naming the pack and the tfm

### Requirement: Self-contained compaction opt-out
`ComplogCompactionOptions` SHALL expose `SelfContained` (default `false`) and the CLI SHALL expose `--self-contained`. When set, no canonicalization or pointer emission occurs and the output matches v1 behavior exactly.

#### Scenario: Self-contained flag
- **WHEN** `glosharp compact-complog build.complog -o out.glocontext --self-contained` is run
- **THEN** the output embeds all references as blobs and uses format v1

### Requirement: Resolver materializes pointer references from packs
`GloContextResolver` SHALL accept format v1 and v2 files. For v2 pointer references it SHALL acquire each referenced pack (per reference-pack-acquisition) once, verify the pack's content hash over its `ref/**/*.dll` files against the manifest pack entry's `sha256`, and materialize `MetadataReference`s for pointed files from the verified snapshot, preserving order, display names, aliases, and `embedInteropTypes` exactly as for blob references. A manifest pack entry without a valid content hash SHALL be rejected.

#### Scenario: v2 file resolves with packs available locally
- **WHEN** a v2 `.glocontext` is opened on a machine whose NuGet global packages folder contains the referenced packs
- **THEN** `Resolve()` returns references materialized from the pack files with no network access

#### Scenario: Content hash mismatch fails loudly
- **WHEN** an acquired pack's content hash does not equal the manifest pack entry's `sha256`
- **THEN** `Open`/`Resolve` throws `InvalidDataException` naming the pack id and version, its location, and both hash values

#### Scenario: Pointed file absent from verified pack
- **WHEN** a pointer's `path` does not exist in the verified pack contents
- **THEN** `Open`/`Resolve` throws `InvalidDataException` naming the pack and the missing path

#### Scenario: Missing pack produces actionable error
- **WHEN** a referenced pack cannot be acquired from any source
- **THEN** the thrown exception names the pack id and version, lists the locations searched, and states remedies (restore a project targeting that framework, retry with network, or request a self-contained artifact)

#### Scenario: v1 files unaffected
- **WHEN** a v1 `.glocontext` is opened by the v2-capable resolver
- **THEN** it resolves exactly as before with no pack acquisition
