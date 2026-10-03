## ADDED Requirements

### Requirement: Gallery renders the standalone renderer's own output
The gallery SHALL publish a page per theme showing the committed `glosharp render` HTML fixtures verbatim, each wrapped in a case section. Without it the standalone renderer is the only render path with no browser coverage — which is how it shipped popups that no selector could reveal and anchor names that collided between fragments.

#### Scenario: Standalone pages present
- **WHEN** the gallery is built
- **THEN** a `standalone-<theme>` page exists per built-in theme, containing every fixture that has HTML committed for that theme

#### Scenario: Missing HTML fixtures are loud
- **WHEN** no HTML fixture exists for a fixture and theme
- **THEN** the build logs the skip, and fails outright if a whole page would be empty

#### Scenario: Fragments coexist on one page
- **WHEN** a standalone page renders many fragments
- **THEN** each fragment's popups anchor to their own tokens

### Requirement: Gallery hosts the GitBook webframe shell
The gallery SHALL publish the real webframe shell (`renderFrameShell()` output, unmodified) plus hash-keyed artifacts, and pages that embed one iframe per case with a host script implementing GitBook's webframe contract — answering `@webframe.ready` with the case's `data` and applying every `@webframe.resize` to the iframe's height. Observable host state (answered, reported height, resize count) SHALL be exposed on the iframe element so tests can wait on it. The host script SHALL implement nothing beyond that contract.

The artifacts SHALL be the committed `glosharp render` HTML fixtures, keyed by `snippetKey` of the fixture source exactly as CI would key them — the real production combination, still with no .NET at test time. The host script SHALL be the one the package ships for its own local preview, so GitBook's contract has a single definition.

#### Scenario: Frame cases published
- **WHEN** the gallery is built
- **THEN** a `gitbook-frame` page contains one iframe per fixture, each with a case identifier and the state GitBook would send

#### Scenario: Artifacts keyed as CI keys them
- **WHEN** the gallery publishes an artifact for a fixture
- **THEN** it is written to `gitbook-artifacts/<theme>/<snippetKey(source)>.html` and is the CLI's own render output

#### Scenario: One definition of the host contract
- **WHEN** the gallery writes its host script
- **THEN** it writes the script exported by the package, not a copy

#### Scenario: Empty-lookup cases present
- **WHEN** the gallery is built
- **THEN** it contains a case with no artifacts URL, and a separate page containing a case whose artifact is deliberately absent

#### Scenario: Frame page build is fixture-only
- **WHEN** the gallery build script runs
- **THEN** the frame page and its artifacts are built from committed fixture JSON and package build outputs, without invoking the GloSharp CLI
