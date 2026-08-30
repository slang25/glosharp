## ADDED Requirements

### Requirement: Committed standalone-renderer HTML fixtures
The repository SHALL contain committed `glosharp render` output for every curated sample, in every built-in theme, at `tests/rendering/fixtures/html/<sample>.<theme>.html`. These are what gives the standalone renderer browser-level coverage: it is the only render path whose markup and CSS ship together, and the only one a GitBook reader sees.

#### Scenario: One fixture per sample and theme
- **WHEN** the fixture set is enumerated
- **THEN** each curated sample has one HTML fixture per built-in theme

#### Scenario: Rendering tests still run without the .NET SDK
- **WHEN** the gallery build or Playwright suite runs on a machine without the .NET SDK
- **THEN** the standalone pages and the GitBook artifacts are produced from committed HTML fixtures alone

## MODIFIED Requirements

### Requirement: Fixture drift check
A check SHALL regenerate fixtures — both the `GloSharpResult` JSON and the per-theme HTML — to memory and compare them against the committed fixtures, failing with a per-file diff summary when they diverge. It SHALL also report committed fixtures that no current sample accounts for. The check SHALL run in CI in a job that has the .NET SDK available.

#### Scenario: Drift fails CI
- **WHEN** the CLI's output for any curated sample no longer matches the committed fixture
- **THEN** the drift check exits non-zero and reports which fixtures diverged

#### Scenario: Renderer change surfaces as an HTML diff
- **WHEN** a change to the standalone renderer alters its markup or CSS
- **THEN** the affected HTML fixtures change, making the difference reviewable in the diff

#### Scenario: Orphaned fixtures reported
- **WHEN** a sample is removed but its fixtures are still committed
- **THEN** the drift check reports them as orphaned

#### Scenario: No drift passes silently
- **WHEN** regenerated output matches all committed fixtures
- **THEN** the drift check exits zero
