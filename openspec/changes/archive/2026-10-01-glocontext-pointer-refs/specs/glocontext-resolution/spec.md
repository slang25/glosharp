## MODIFIED Requirements

### Requirement: Resolved references match compacted blobs
The resolver SHALL materialize each `MetadataReference` either from the `refs/<hash>.dll` blob identified in the manifest or, for v2 pointer entries, from the verified bytes of the pointed targeting-pack file. Reference order in the result SHALL match the manifest order. Display names and alias lists SHALL be preserved from the manifest. Blob and pointer references SHALL be indistinguishable to consumers of the resolution result.

#### Scenario: Reference order preserved
- **WHEN** `Resolve` is called
- **THEN** the returned references appear in the same order as the manifest's `references` array for the selected compilation, regardless of whether individual entries are blobs or pointers

#### Scenario: Aliases preserved
- **WHEN** a compilation references an assembly with external aliases in the manifest
- **THEN** the returned `MetadataReference` has those aliases set

#### Scenario: Mixed blob and pointer references
- **WHEN** a v2 manifest mixes pointer references (framework) and blob references (NuGet libraries)
- **THEN** `Resolve` returns a single ordered reference list drawing from both sources
