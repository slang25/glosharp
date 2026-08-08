## ADDED Requirements

### Requirement: Self-contained flag on compact-complog
The `compact-complog` command SHALL accept a `--self-contained` flag that disables pointer canonicalization and embeds every reference as a blob, producing a format v1 file. Help text SHALL describe it as the option for artifacts that must resolve offline with no pack downloads.

#### Scenario: Self-contained output
- **WHEN** `glosharp compact-complog build.complog -o out.glocontext --self-contained` is run
- **THEN** the output contains no pointer references and its header format version is `0x01`

## MODIFIED Requirements

### Requirement: Compaction summary on stderr
The `compact-complog` command SHALL write a human-readable summary to stderr after a successful compaction, showing input size, output size, reduction percentage, reference counts before and after dedupe, refasmer rewrite count, pointer count with the distinct packs referenced (id and version), and the counts and sizes of analyzers, original sources, and generated sources affected. When canonical packs were unavailable and references fell back to embedding, the summary SHALL include a warning line naming each unavailable pack. Stdout SHALL be empty on success.

#### Scenario: Summary present by default
- **WHEN** `glosharp compact-complog build.complog -o out.glocontext` completes successfully
- **THEN** stderr contains the summary lines described above and stdout is empty

#### Scenario: Summary reports pointers and packs
- **WHEN** compaction canonicalizes framework references
- **THEN** the summary reports the pointer count and lists each referenced pack as `<id>/<version>`

#### Scenario: Quiet suppresses summary
- **WHEN** `--quiet` is passed and compaction completes successfully
- **THEN** neither stdout nor stderr contains the summary
