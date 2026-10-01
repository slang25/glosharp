# rendering-gallery Specification

## Purpose
TBD - created by archiving change rendering-feedback-loop. Update Purpose after archive.
## Requirements
### Requirement: Gallery renders the full feature matrix from fixtures
A gallery build script SHALL produce static HTML pages that render every fixture through both Node render paths — the Shiki transformer (`@glosharp/shiki`) and the Expressive Code plugin (`@glosharp/expressive-code`) — in both dark and light themes. Each rendered case SHALL be addressable via a stable identifier attribute (e.g. `data-gallery-case="<path>/<fixture>/<theme>"`) so tests and screenshots can target it deterministically.

#### Scenario: Both render paths present
- **WHEN** the gallery is built from the fixture set
- **THEN** every fixture appears rendered by the Shiki transformer and by the Expressive Code plugin, each in dark and light themes, each tagged with its case identifier

#### Scenario: Gallery build is fixture-only
- **WHEN** the gallery build script runs
- **THEN** it reads only committed fixture JSON and package build outputs, and does not invoke the GloSharp CLI

### Requirement: Deterministic rendering environment
The gallery SHALL be visually deterministic across machines and runs: it MUST bundle and use a pinned monospace webfont (no reliance on system fonts), and it MUST provide a mode (query parameter or class) that disables all CSS animations and transitions.

#### Scenario: Same input, same pixels
- **WHEN** the gallery is built twice from the same fixtures and packages and loaded in the same browser/viewport with animations disabled
- **THEN** the rendered pages are pixel-identical

### Requirement: Pinnable popup states
The gallery SHALL provide a debug affordance to force hover popups open without a pointer — for the Shiki path by overriding the hover-gated CSS, and for the Expressive Code path by programmatically triggering the plugin's own show logic. The affordance SHALL live entirely in gallery-side script/CSS; the published packages MUST NOT be modified to support it.

#### Scenario: Pinning a case opens its popups
- **WHEN** the gallery is loaded with the pin affordance targeting a case
- **THEN** that case's hover popups are visible and positioned without any mouse interaction

#### Scenario: Packages unchanged
- **WHEN** the published `@glosharp/shiki` and `@glosharp/expressive-code` outputs are inspected
- **THEN** they contain no gallery- or pin-specific code

### Requirement: Gallery is locally servable
The gallery SHALL be servable with a single npm script (static file server) for interactive human review and for the Playwright suite to target.

#### Scenario: One command to view
- **WHEN** a developer runs the gallery serve script
- **THEN** the gallery is available on a local port and browsable without further setup

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

