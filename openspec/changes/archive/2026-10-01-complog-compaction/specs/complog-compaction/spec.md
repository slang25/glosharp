## ADDED Requirements

### Requirement: GloContext file format v1
The compactor SHALL produce a `.glocontext` file beginning with the ASCII magic bytes `GLOCTX` followed by a one-byte format version (`0x01` for v1), a one-byte flags field (`0x00` in v1), an 8-byte baseline id (all zeros in v1), a 4-byte baseline version (`0` in v1), and 8 reserved zero bytes, followed by a zstd-compressed payload. The payload SHALL decompress to a tar stream containing a `manifest.json` and per-blob `refs/<sha256-hex>.dll` entries.

#### Scenario: Header bytes present
- **WHEN** `ComplogCompactor.Compact` writes an output file
- **THEN** bytes 0–5 are ASCII `GLOCTX`, byte 6 is `0x01`, byte 7 is `0x00`, bytes 8–15 are zero, bytes 16–19 are zero, bytes 20–27 are zero, and bytes 28+ are a valid zstd stream

#### Scenario: Reserved fields are zero in v1
- **WHEN** the compactor writes a v1 file
- **THEN** the baseline id and baseline version fields are all zero

#### Scenario: Payload contains manifest and refs
- **WHEN** the zstd payload is decompressed
- **THEN** the resulting tar contains a `manifest.json` at the root and zero or more `refs/<sha256-hex>.dll` entries, and no other entries

### Requirement: Zstd container with long-range matching
The compactor SHALL compress the payload using zstd with a window log of at least 27 (128 MiB window) and a default compression level of 19. The compression level SHALL be configurable via `ZstdLevel` (library) and `--zstd-level` (CLI).

#### Scenario: Default compression settings
- **WHEN** `ComplogCompactor.Compact` is called with default options
- **THEN** the payload is compressed at zstd level 19 with windowLog 27

#### Scenario: Custom compression level
- **WHEN** `ZstdLevel` is set to 3
- **THEN** the payload is compressed at zstd level 3 and the file is still a valid `.glocontext`

### Requirement: Reference-assembly rewriting is mandatory by default
The compactor SHALL rewrite every referenced assembly using `JetBrains.Refasmer` (with `omitNonApiMembers = true`) to strip IL bodies, private members, embedded resources, and embedded PDBs. Opting out via `RewriteReferences = false` (library) or `--no-refasm` (CLI) is a debug option and SHALL be documented as such in help text.

#### Scenario: References rewritten by default
- **WHEN** a complog is compacted with default options
- **THEN** every output reference blob is the result of Refasmer rewriting its input, except where the input is already marked with `ReferenceAssemblyAttribute`

#### Scenario: Already-a-reference-assembly inputs passed through
- **WHEN** a reference in the input complog is marked with `System.Runtime.CompilerServices.ReferenceAssemblyAttribute`
- **THEN** the compactor copies it through unchanged and counts it as not-rewritten in the result

#### Scenario: Refasmer failure aborts compaction
- **WHEN** Refasmer throws while rewriting a reference assembly
- **THEN** the compactor surfaces the error with the offending assembly name and does not leave a partial output file on disk

#### Scenario: Opt out of Refasmer rewriting
- **WHEN** a complog is compacted with `RewriteReferences = false` or `--no-refasm`
- **THEN** referenced assemblies are stored as-is and the result reports zero rewritten references

### Requirement: Analyzer DLLs dropped by default
The compactor SHALL exclude analyzer and source-generator DLL entries from the output archive by default, unless the caller opts in via `DropAnalyzers = false` or `--keep-analyzers`. When analyzers are kept, they are stored as additional entries under `analyzers/<sha256-hex>.dll` in the tar.

#### Scenario: Analyzers dropped by default
- **WHEN** a complog containing analyzer DLLs is compacted with default options
- **THEN** the output archive contains no `analyzers/` entries and the result reports the number of analyzers dropped

#### Scenario: Keep analyzers opt-in
- **WHEN** the compactor is invoked with `DropAnalyzers = false`
- **THEN** each unique analyzer DLL is stored at `analyzers/<sha256-hex>.dll` and referenced from the manifest

### Requirement: Original and generated source trees dropped by default
The compactor SHALL drop both the original project source entries and the generator-produced source entries from the output archive by default, unless the caller opts in via `DropOriginalSources = false` / `--keep-sources` or `DropGeneratedSources = false` / `--keep-generated`.

#### Scenario: Original sources dropped by default
- **WHEN** a complog containing original project sources is compacted with default options
- **THEN** the output archive contains no original-source entries

#### Scenario: Generated sources dropped by default
- **WHEN** a complog containing generator output is compacted with default options
- **THEN** the output archive contains no generator-output source entries

#### Scenario: Keep sources opt-in
- **WHEN** `DropOriginalSources = false` is passed
- **THEN** original source entries are stored under `sources/` in the tar

#### Scenario: Keep generated opt-in
- **WHEN** `DropGeneratedSources = false` is passed
- **THEN** generator output entries are stored under `generated/` in the tar

### Requirement: References deduplicated by content hash
The compactor SHALL compute a SHA-256 hash over each reference assembly's bytes (post-Refasmer where applicable) and store each unique hash once in the output archive. Per-compilation reference lists in the manifest SHALL reference blobs by hash.

#### Scenario: Same reference across projects stored once
- **WHEN** a multi-project complog lists the same reference assembly in two or more compilations
- **THEN** the output archive contains a single `refs/<hash>.dll` entry and each compilation's reference list in the manifest points at that entry

#### Scenario: Dedupe counts reported
- **WHEN** a complog with 3 compilations sharing 40 references each (50 unique after dedupe) is compacted
- **THEN** the result's `ReferencesBefore` is 120 and `ReferencesAfter` is 50

### Requirement: Manifest captures compilation and parse options
The `manifest.json` SHALL contain, for each compilation, enough information to reconstruct a `CSharpCompilationOptions`, a `CSharpParseOptions`, a target framework string, and the ordered list of `MetadataReference` entries. The manifest SHALL use sorted JSON keys so that bit-identical inputs produce bit-identical outputs.

#### Scenario: Manifest round-trips compilation options
- **WHEN** a `.glocontext` is opened by `GloContextResolver` and resolved
- **THEN** the returned `CSharpCompilationOptions` exposes the same `OutputKind`, `NullableContextOptions`, `Platform`, `AllowUnsafe`, and `SpecificDiagnosticOptions` as the original complog's compilation options

#### Scenario: Manifest round-trips parse options
- **WHEN** a `.glocontext` is opened and resolved
- **THEN** the returned `CSharpParseOptions` exposes the same `LanguageVersion`, `DocumentationMode`, `PreprocessorSymbolNames`, and `Features` as the original

#### Scenario: Deterministic output
- **WHEN** the same complog is compacted twice with the same options
- **THEN** the two output files are byte-identical

### Requirement: Compaction result reports sizes, counts, and drops
`ComplogCompactor.Compact` SHALL return a `ComplogCompactionResult` with the input size in bytes, output size in bytes, reference count before dedupe, reference count after dedupe, number of references rewritten by Refasmer, number of analyzers dropped, number of original sources dropped, and number of generated sources dropped.

#### Scenario: Result reflects drops
- **WHEN** a complog containing 7 analyzer DLLs, 126 original source files, and 12 generator outputs is compacted with default options
- **THEN** the result reports `AnalyzersDropped = 7`, `OriginalSourcesDropped = 126`, `GeneratedSourcesDropped = 12`

### Requirement: Library API is a static Compact method on ComplogCompactor
`GloSharp.Core` SHALL expose `ComplogCompactor.Compact(string inputPath, string outputPath, ComplogCompactionOptions options)` as a static method returning `ComplogCompactionResult`. `ComplogCompactionOptions` SHALL default to: `RewriteReferences = true`, `DropAnalyzers = true`, `DropOriginalSources = true`, `DropGeneratedSources = true`, `ZstdLevel = 19`, `ZstdWindowLog = 27`.

#### Scenario: Default options
- **WHEN** `ComplogCompactor.Compact(input, output, new ComplogCompactionOptions())` is called
- **THEN** references are rewritten via Refasmer, analyzers and both kinds of sources are dropped, and the output uses zstd level 19 with windowLog 27

#### Scenario: Invalid input path
- **WHEN** `Compact` is called with a path to a file that does not exist
- **THEN** it throws `FileNotFoundException` with the offending path

#### Scenario: Output path unwritable
- **WHEN** `Compact` is called with an output path whose directory does not exist or is not writable
- **THEN** it throws an `IOException` and no partial output file remains on disk

### Requirement: Atomic output write
The compactor SHALL write to a temporary file in the target directory and rename it over the destination on success. On any failure, the temporary file SHALL be deleted and the destination SHALL NOT exist or SHALL be left in its pre-existing state.

#### Scenario: Partial write cleanup
- **WHEN** compaction fails mid-write (e.g. Refasmer throws)
- **THEN** no file exists at the output path unless one existed before, in which case the pre-existing file is unchanged
