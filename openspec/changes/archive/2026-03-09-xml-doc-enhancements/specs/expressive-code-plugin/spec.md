## MODIFIED Requirements

### Requirement: Inject popup HTML in postprocessRenderedBlock
The `postprocessRenderedBlock` hook SHALL inject hover popup HTML containers with CSS anchor positioning into the rendered output. Popup content SHALL include structured doc sections when available: summary text, a parameter list, return description, remarks, examples, and exception list. Each section SHALL be rendered in a distinct styled container.

#### Scenario: Popup with summary only
- **WHEN** a hover has `docs` with only `summary` populated
- **THEN** the popup renders the summary in a `.glosharp-popup-docs` div, visually identical to the current behavior

#### Scenario: Popup with params and returns
- **WHEN** a hover has `docs` with `summary`, `params`, and `returns`
- **THEN** the popup renders the summary, followed by a params section listing each parameter name and description, followed by a returns section

#### Scenario: Popup with all doc sections
- **WHEN** a hover has `docs` with `summary`, `params`, `returns`, `remarks`, `examples`, and `exceptions`
- **THEN** the popup renders all sections in order: summary, params, returns, remarks, examples, exceptions

#### Scenario: Popup without docs
- **WHEN** a hover has `docs` as null
- **THEN** the popup renders only the type signature code, with no docs section

#### Scenario: Popup container injected
- **WHEN** rendering completes for a code block with hover annotations
- **THEN** the rendered HTML contains popup `<div>` elements with `position-anchor` CSS and structured display parts content

### Requirement: Theme-aware styling for doc sections
The plugin SHALL define CSS classes for each doc section (`.glosharp-popup-params`, `.glosharp-popup-returns`, `.glosharp-popup-remarks`, `.glosharp-popup-example`, `.glosharp-popup-exceptions`) with styles consistent with the existing popup design. Parameter names SHALL be visually distinct (e.g., monospace or bold).

#### Scenario: Param list styling
- **WHEN** a popup with params is rendered
- **THEN** each parameter is displayed with its name in a distinct style (code font) followed by its description

#### Scenario: Section separators
- **WHEN** a popup has multiple doc sections
- **THEN** each section is visually separated (consistent with the existing `.glosharp-popup-docs` border-top pattern)
