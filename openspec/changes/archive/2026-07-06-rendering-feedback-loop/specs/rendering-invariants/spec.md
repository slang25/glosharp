# rendering-invariants Specification

## ADDED Requirements

### Requirement: Popup adjacency on hover
The Playwright suite SHALL assert that hovering a hover-annotated token makes its popup visible within a bounded time, and that the popup's bounding box is adjacent to the token's bounding box (gap and horizontal offset within a defined pixel tolerance) for both render paths.

#### Scenario: Popup appears next to its token
- **WHEN** the pointer hovers a hover token in a gallery case
- **THEN** the popup becomes visible and its bounding box is within the adjacency tolerance of the token's bounding box

### Requirement: Popup tracks its token during scroll
The suite SHALL assert that an open popup remains adjacent to its token after the code container is scrolled horizontally and after the page is scrolled vertically, for the Expressive Code path (JS-positioned popups). This encodes the class of regression fixed in PR #91.

#### Scenario: Horizontal container scroll
- **WHEN** a popup is open on a long-line case and the code container is scrolled horizontally
- **THEN** the popup's position tracks the token, remaining within the adjacency tolerance (or the popup hides, if hiding is the specified behavior for the scroll distance)

#### Scenario: Vertical page scroll
- **WHEN** a popup is open and the page scrolls vertically
- **THEN** the popup remains within the adjacency tolerance of its token

### Requirement: Viewport containment at mobile widths
The suite SHALL assert at a mobile viewport (390px wide) and a tablet viewport (768px wide) that any opened popup's bounding box lies fully within the visual viewport.

#### Scenario: Popup near the right edge
- **WHEN** a popup opens for a token near the right edge of a 390px viewport
- **THEN** the popup's bounding box does not extend beyond any viewport edge

### Requirement: Popup hover lifecycle
The suite SHALL assert the interaction contract of popups: a popup stays open while the pointer moves from the token onto the popup itself, and closes after the pointer leaves both token and popup (respecting the hide delay).

#### Scenario: Moving onto the popup keeps it open
- **WHEN** the pointer moves from a hover token directly onto its open popup
- **THEN** the popup remains visible

#### Scenario: Leaving closes the popup
- **WHEN** the pointer leaves both the token and the popup
- **THEN** the popup is hidden after the configured delay

### Requirement: Re-initialization after page swap
The suite SHALL assert that Expressive Code popups still function after a simulated Astro view transition: replacing the gallery content DOM and dispatching `astro:page-load` MUST leave hover popups working on the new content.

#### Scenario: Hover works after simulated view transition
- **WHEN** gallery content is replaced and `astro:page-load` is dispatched
- **THEN** hovering a token in the new content opens its popup

### Requirement: Console cleanliness
The suite SHALL fail if loading any gallery page, or executing any test interaction, produces a console error or an uncaught page error.

#### Scenario: Clean load
- **WHEN** each gallery page is loaded and its cases exercised
- **THEN** zero console errors and zero uncaught exceptions are observed

### Requirement: Cross-browser execution
The suite SHALL run in both Chromium and Firefox. In browsers without CSS Anchor Positioning support, the Shiki path's `@supports not` fallback MUST still yield a visible, sanely positioned popup on hover.

#### Scenario: Fallback path renders popups
- **WHEN** the adjacency and lifecycle tests run in a browser lacking CSS Anchor Positioning
- **THEN** popups are still visible on hover and positioned within the (relaxed) fallback tolerance
