# rendering-invariants Specification

## Purpose
TBD - created by archiving change rendering-feedback-loop. Update Purpose after archive.
## Requirements
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
The suite SHALL fail if loading any gallery page, or executing any test interaction, produces a console error or an uncaught page error. A spec whose subject *is* a browser-level failure MAY narrow this with an explicit per-spec allowlist of message patterns; such a spec SHALL assert the intended behaviour positively, and the gallery SHALL isolate the failing case on its own page so no other spec's allowlist has to widen.

#### Scenario: Clean load
- **WHEN** each gallery page is loaded and its cases exercised
- **THEN** zero console errors and zero uncaught exceptions are observed

#### Scenario: Deliberate failure allowed only where it is the subject
- **WHEN** the unpublished-artifact spec runs, whose 404 the browser reports as a console error
- **THEN** that spec's allowlist tolerates only that pattern, and every other spec still fails on any console error

### Requirement: Cross-browser execution
The suite SHALL run in both Chromium and Firefox. In browsers without CSS Anchor Positioning support, the Shiki path's `@supports not` fallback MUST still yield a visible, sanely positioned popup on hover.

#### Scenario: Fallback path renders popups
- **WHEN** the adjacency and lifecycle tests run in a browser lacking CSS Anchor Positioning
- **THEN** popups are still visible on hover and positioned within the (relaxed) fallback tolerance

### Requirement: Standalone renderer invariants
The suite SHALL assert, against the committed `glosharp render` output: no popup is open before hovering; hovering a token opens its popup; the popup is adjacent to that token; the popup closes when the pointer leaves; the popup stays inside a mobile viewport; and the rendered fragments contain no `<script>` elements.

#### Scenario: Hover opens the popup
- **WHEN** a token on a standalone page is hovered
- **THEN** its popup becomes visible, having been hidden beforehand

#### Scenario: Popup belongs to its token
- **WHEN** a popup opens on a page carrying many fragments
- **THEN** it is adjacent to the token that was hovered, not to a token in another fragment

### Requirement: Webframe invariants
The suite SHALL assert, against the real shell driven through GitBook's message contract: every frame is answered and reports a height; a published snippet is found by hashing its own content and renders its hover tokens; the reported height covers the rendered fragment without wildly overshooting it; every hover popup ends up fully inside the frame's box, at least one of them only because the frame grew; the frame returns to its resting height once the pointer leaves; and both empty-lookup paths render the snippet as plain code with an explanatory note.

#### Scenario: Popup containment inside the frame
- **WHEN** each hover token in a frame case is hovered
- **THEN** the popup's box lies within the iframe's box

#### Scenario: Growth is exercised, not assumed
- **WHEN** every hover token in a frame case has been hovered
- **THEN** at least one popup required the frame to grow beyond its resting height

#### Scenario: Popups open below their token
- **WHEN** each hover token in a frame case is hovered
- **THEN** the popup's top edge is at or below the token's bottom edge — containment alone passes while a popup sits on top of the code

#### Scenario: A late host is still answered
- **WHEN** a frame is created and a listener attached only after it has loaded
- **THEN** the frame is still answered

#### Scenario: Growth is not undone by the host applying it
- **WHEN** a token whose popup needs extra room is hovered and the pointer is left there past the shell's re-measure debounce
- **THEN** the frame is still grown and the popup is still contained

#### Scenario: Degradation is visible
- **WHEN** a case has no artifacts URL, or its artifact is not published
- **THEN** the snippet is rendered as plain code with a note naming the reason

