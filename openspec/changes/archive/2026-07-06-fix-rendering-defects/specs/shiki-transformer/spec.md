# shiki-transformer Delta

## MODIFIED Requirements

### Requirement: Inject hover popups in root hook
The `root` hook SHALL walk the HAST tree, match token positions to hover data from the glosharp result, and wrap target tokens with hover popup elements using CSS anchor positioning. The popup `<div class="glosharp-popup">` SHALL be a child of the `<span class="glosharp-hover">` wrapper (not a sibling), so that `:hover` on the wrapper keeps the popup open while the pointer is over the popup, and so the popup can be positioned relative to the wrapper when anchor positioning is unavailable.

#### Scenario: Token with hover data
- **WHEN** a token at line 0, character 4 has associated hover data
- **THEN** the HAST tree contains a `<span class="glosharp-hover">` with `anchor-name: --glosharp-N` wrapping the token, containing a child `<div class="glosharp-popup">` with `position-anchor: --glosharp-N` and the formatted hover text

#### Scenario: Pointer moves onto the popup
- **WHEN** the pointer moves from the hovered token onto its open popup
- **THEN** the popup remains open, because the popup is inside the wrapper's `:hover` subtree

### Requirement: CSS anchor positioning for popups
Hover popups SHALL use CSS anchor positioning (`anchor-name`, `position-anchor`, `position-area: top`) and be shown/hidden via `:hover` CSS pseudo-class. No JavaScript SHALL be required. The stylesheet SHALL include an `@supports not` fallback for browsers without CSS Anchor Positioning that positions the popup absolutely relative to its hover wrapper (directly above or below the token), so popups remain usable — degraded positioning is acceptable, a popup detached from its token is not.

#### Scenario: Hover popup visibility
- **WHEN** the rendered HTML is viewed in a browser
- **THEN** hovering over a token with hover data shows a popup positioned above the token using CSS anchoring, with no JS execution

#### Scenario: Browser without anchor positioning
- **WHEN** the rendered HTML is viewed in a browser where `anchor-name` is unsupported (or the anchor properties are stripped)
- **THEN** hovering the token still shows the popup adjacent to the hover wrapper, within a relaxed positional tolerance
