## ADDED Requirements

### Requirement: Compact-complog command
The CLI SHALL accept a `compact-complog` command that reads a `.complog` file and writes a `.glocontext` file to the path given by `-o` / `--output`.

#### Scenario: Compact with defaults
- **WHEN** `glosharp compact-complog build.complog -o build.glocontext` is run against an existing complog
- **THEN** the command writes a `.glocontext` file at `build.glocontext` and exits with code 0

#### Scenario: Input complog missing
- **WHEN** `glosharp compact-complog missing.complog -o out.glocontext` is run and the input does not exist
- **THEN** the command writes an error to stderr identifying the missing input and exits with a non-zero code

#### Scenario: Output path not writable
- **WHEN** the output path cannot be written
- **THEN** the command writes an error to stderr and exits with a non-zero code, and no partial output file remains on disk

### Requirement: Keep-analyzers flag (debug)
The `compact-complog` command SHALL accept a `--keep-analyzers` flag that preserves analyzer and source-generator DLL entries in the output archive under `analyzers/`. This flag SHALL be documented in help text as a debug option; the default (drop) is the recommended configuration.

#### Scenario: Keep analyzers
- **WHEN** `glosharp compact-complog build.complog -o out.glocontext --keep-analyzers` is run against a complog containing analyzer DLLs
- **THEN** the output contains those analyzer entries under `analyzers/` in the payload tar

### Requirement: Keep-sources and keep-generated flags (debug)
The `compact-complog` command SHALL accept `--keep-sources` (preserves original project source entries) and `--keep-generated` (preserves generator-produced source entries). Both SHALL be documented as debug options.

#### Scenario: Keep original sources
- **WHEN** `--keep-sources` is passed
- **THEN** original source entries are stored under `sources/` in the payload tar

#### Scenario: Keep generated sources
- **WHEN** `--keep-generated` is passed
- **THEN** generator output entries are stored under `generated/` in the payload tar

### Requirement: No-refasm flag (debug)
The `compact-complog` command SHALL accept a `--no-refasm` flag that disables Refasmer rewriting of referenced assemblies. This flag SHALL be documented as a debug option; the default (rewrite) is the recommended configuration.

#### Scenario: Disable refasm
- **WHEN** `--no-refasm` is passed
- **THEN** referenced assemblies in the output are the input bytes unchanged (after dedupe)

### Requirement: Zstd level flag
The `compact-complog` command SHALL accept a `--zstd-level <n>` option to override the default compression level (19). Valid values are 1 through 22.

#### Scenario: Custom level
- **WHEN** `--zstd-level 3` is passed
- **THEN** the payload is compressed at level 3

#### Scenario: Invalid level
- **WHEN** `--zstd-level 0` or `--zstd-level 23` is passed
- **THEN** the command writes an error to stderr and exits non-zero

### Requirement: Compaction summary on stderr
The `compact-complog` command SHALL write a human-readable summary to stderr after a successful compaction, showing input size, output size, reduction percentage, reference counts before and after dedupe, refasmer rewrite count, and the counts and sizes of analyzers, original sources, and generated sources affected. Stdout SHALL be empty on success.

#### Scenario: Summary present by default
- **WHEN** `glosharp compact-complog build.complog -o out.glocontext` completes successfully
- **THEN** stderr contains the summary lines described above and stdout is empty

#### Scenario: Quiet suppresses summary
- **WHEN** `--quiet` is passed and compaction completes successfully
- **THEN** neither stdout nor stderr contains the summary

### Requirement: Exit code reflects compaction success
The `compact-complog` command SHALL exit with code 0 when the output file is written successfully and with a non-zero code when input cannot be read, output cannot be written, Refasmer fails on a reference, or the input is not a recognizable complog.

#### Scenario: Exit 0 on success
- **WHEN** compaction completes and the output file is written
- **THEN** the command exits with code 0

#### Scenario: Non-zero exit on Refasmer failure
- **WHEN** Refasmer throws while rewriting a reference and `--no-refasm` was not passed
- **THEN** the command exits non-zero and writes the offending assembly name to stderr

### Requirement: --complog option accepts .glocontext
The existing `--complog` option on `process`, `verify`, and `render` SHALL accept both `.complog` and `.glocontext` files, auto-detecting by the file's leading bytes. Users SHALL NOT need to pass a different flag for `.glocontext` files.

#### Scenario: Process against a .glocontext
- **WHEN** `glosharp process snippet.cs --complog build.glocontext` is run against a valid `.glocontext`
- **THEN** references and options are resolved via `GloContextResolver` and processing proceeds

#### Scenario: Process against a .complog
- **WHEN** `glosharp process snippet.cs --complog build.complog` is run
- **THEN** references and options are resolved via `ComplogResolver` and processing proceeds

#### Scenario: Unrecognized file
- **WHEN** `--complog` points at a file whose leading bytes match neither `GLOCTX` nor the zip magic
- **THEN** the CLI exits non-zero with an error that names both expected formats
