# rendering-gallery Specification

## ADDED Requirements

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
