# render-fixtures Specification

## Purpose
TBD - created by archiving change rendering-feedback-loop. Update Purpose after archive.
## Requirements
### Requirement: Committed GloSharpResult fixtures
The repository SHALL contain committed `GloSharpResult` JSON fixture files for a curated set of sample `.cs` files, covering at minimum: basic hovers, XML doc comments (summary, params, returns, exceptions), diagnostics at error and warning severity, a multi-line error span, completions, highlight/focus/diff annotations, custom tags, and a sample with lines long enough to force horizontal scrolling in a code container. (Info-severity diagnostics are not producible from a plain compilation; info styling remains covered by unit tests that render synthetic results.)

#### Scenario: Rendering tests run without the .NET SDK
- **WHEN** the rendering gallery build or Playwright suite runs on a machine without the .NET SDK
- **THEN** all rendering artifacts are produced from committed fixture JSON alone, with no invocation of the GloSharp CLI

#### Scenario: Feature matrix coverage
- **WHEN** the fixture set is enumerated
- **THEN** every feature state listed in this requirement is represented by at least one fixture

### Requirement: Fixture regeneration script
A script SHALL regenerate all fixtures by running the GloSharp CLI over the curated sample list and writing normalized JSON. Normalization MUST remove or pin volatile fields (tool version, absolute paths, timings) so that regenerated output is byte-identical unless the semantic output of the pipeline changed.

#### Scenario: Regeneration is deterministic
- **WHEN** the regeneration script is run twice against an unchanged CLI and unchanged samples
- **THEN** the fixture files are byte-identical across both runs

#### Scenario: Semantic change surfaces as fixture diff
- **WHEN** a change to the C# core alters hover text, diagnostics, or any other `GloSharpResult` content and the regeneration script is run
- **THEN** the affected fixture files change, making the semantic difference reviewable in the diff

### Requirement: Fixture drift check
A check SHALL regenerate fixtures — both the `GloSharpResult` JSON and the per-theme HTML — to memory and compare them against the committed fixtures, failing with a per-file diff summary when they diverge. It SHALL also report committed fixtures that no current sample accounts for. The check SHALL run in CI in a job that has the .NET SDK available.

#### Scenario: Drift fails CI
- **WHEN** the CLI's output for any curated sample no longer matches the committed fixture
- **THEN** the drift check exits non-zero and reports which fixtures diverged

#### Scenario: Renderer change surfaces as an HTML diff
- **WHEN** a change to the standalone renderer alters its markup or CSS
- **THEN** the affected HTML fixtures change, making the difference reviewable in the diff

#### Scenario: Unrecognisable render output fails the run
- **WHEN** the CLI's render output does not contain the expected fragment marker
- **THEN** the script fails and names the sample, rather than committing a truncated fixture

#### Scenario: Orphaned fixtures reported
- **WHEN** a sample is removed but its fixtures are still committed
- **THEN** the drift check reports them as orphaned

#### Scenario: No drift passes silently
- **WHEN** regenerated output matches all committed fixtures
- **THEN** the drift check exits zero

### Requirement: Committed standalone-renderer HTML fixtures
The repository SHALL contain committed `glosharp render` output for every curated sample, in every built-in theme, at `tests/rendering/fixtures/html/<sample>.<theme>.html`. These are what gives the standalone renderer browser-level coverage: it is the only render path whose markup and CSS ship together, and the only one a GitBook reader sees.

#### Scenario: One fixture per sample and theme
- **WHEN** the fixture set is enumerated
- **THEN** each curated sample has one HTML fixture per built-in theme

#### Scenario: Rendering tests still run without the .NET SDK
- **WHEN** the gallery build or Playwright suite runs on a machine without the .NET SDK
- **THEN** the standalone pages and the GitBook artifacts are produced from committed HTML fixtures alone

