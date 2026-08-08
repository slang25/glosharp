## MODIFIED Requirements

### Requirement: Reference-assembly rewriting is mandatory by default
The compactor SHALL rewrite every embedded referenced assembly using `JetBrains.Refasmer` invoked with `omitNonApiMembers = true` (both the `AllowPublic` filter argument and the `MakeRefasm` parameter) to strip IL bodies, private members, embedded resources, and embedded PDBs. References canonicalized to pack pointers are not rewritten (their bytes are not embedded). Detection of inputs that are already reference assemblies SHALL recognize `System.Runtime.CompilerServices.ReferenceAssemblyAttribute` whether its constructor is a `MemberReference` (attribute type defined in another assembly) or a `MethodDefinition` (attribute type defined in the same assembly, e.g. `System.Runtime.dll`). Opting out via `RewriteReferences = false` (library) or `--no-refasm` (CLI) is a debug option and SHALL be documented as such in help text.

#### Scenario: References rewritten by default
- **WHEN** a complog is compacted with default options
- **THEN** every embedded output reference blob is the result of Refasmer rewriting its input, except where the input is already marked with `ReferenceAssemblyAttribute`

#### Scenario: Assemblies with private anonymous-type members rewrite cleanly
- **WHEN** a complog references an assembly containing private fields typed as compiler-generated anonymous types (e.g. Microsoft.EntityFrameworkCore, FluentAssertions)
- **THEN** Refasmer rewriting succeeds because non-API members are omitted

#### Scenario: Already-a-reference-assembly inputs passed through
- **WHEN** a reference in the input complog is marked with `System.Runtime.CompilerServices.ReferenceAssemblyAttribute`
- **THEN** the compactor copies it through unchanged and counts it as not-rewritten in the result

#### Scenario: MethodDef-declared attribute recognized
- **WHEN** a reference declares `ReferenceAssemblyAttribute` via a constructor that is a `MethodDefinition` in the same assembly (the `System.Runtime.dll` case)
- **THEN** the compactor treats it as a reference assembly and passes it through unchanged

#### Scenario: Refasmer failure aborts compaction
- **WHEN** Refasmer throws while rewriting a reference assembly
- **THEN** the compactor surfaces the error with the offending assembly name and does not leave a partial output file on disk

#### Scenario: Opt out of Refasmer rewriting
- **WHEN** a complog is compacted with `RewriteReferences = false` or `--no-refasm`
- **THEN** embedded referenced assemblies are stored as-is and the result reports zero rewritten references

### Requirement: Compaction result reports sizes, counts, and drops
`ComplogCompactor.Compact` SHALL return a `ComplogCompactionResult` with the input size in bytes, output size in bytes, reference count before dedupe, reference count after dedupe, number of references rewritten by Refasmer, number of references canonicalized to pack pointers, the list of packs referenced by pointers, number of analyzers dropped, number of original sources dropped, and number of generated sources dropped.

#### Scenario: Result reflects drops
- **WHEN** a complog containing 7 analyzer DLLs, 126 original source files, and 12 generator outputs is compacted with default options
- **THEN** the result reports `AnalyzersDropped = 7`, `OriginalSourcesDropped = 126`, `GeneratedSourcesDropped = 12`

#### Scenario: Result reflects pointers
- **WHEN** a complog whose references include targeting-pack assemblies is compacted with default options and the canonical packs are available
- **THEN** the result reports a non-zero pointer count and the distinct pack ids and versions used

### Requirement: Library API is a static Compact method on ComplogCompactor
`GloSharp.Core` SHALL expose `ComplogCompactor.Compact(string inputPath, string outputPath, ComplogCompactionOptions options)` as a static method returning `ComplogCompactionResult`. `ComplogCompactionOptions` SHALL default to: `RewriteReferences = true`, `DropAnalyzers = true`, `DropOriginalSources = true`, `DropGeneratedSources = true`, `SelfContained = false`, `ZstdLevel = 19`, `ZstdWindowLog = 27`.

#### Scenario: Default options
- **WHEN** `ComplogCompactor.Compact(input, output, new ComplogCompactionOptions())` is called
- **THEN** framework references are canonicalized to pointers where the canonical packs are available, remaining references are rewritten via Refasmer, analyzers and both kinds of sources are dropped, and the output uses zstd level 19 with windowLog 27

#### Scenario: Invalid input path
- **WHEN** `Compact` is called with a path to a file that does not exist
- **THEN** it throws `FileNotFoundException` with the offending path

#### Scenario: Output path unwritable
- **WHEN** `Compact` is called with an output path whose directory does not exist or is not writable
- **THEN** it throws an `IOException` and no partial output file remains on disk
