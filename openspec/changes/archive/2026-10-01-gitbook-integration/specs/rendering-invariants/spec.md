## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Console cleanliness
The suite SHALL fail if loading any gallery page, or executing any test interaction, produces a console error or an uncaught page error. A spec whose subject *is* a browser-level failure MAY narrow this with an explicit per-spec allowlist of message patterns; such a spec SHALL assert the intended behaviour positively, and the gallery SHALL isolate the failing case on its own page so no other spec's allowlist has to widen.

#### Scenario: Clean load
- **WHEN** each gallery page is loaded and its cases exercised
- **THEN** zero console errors and zero uncaught exceptions are observed

#### Scenario: Deliberate failure allowed only where it is the subject
- **WHEN** the unpublished-artifact spec runs, whose 404 the browser reports as a console error
- **THEN** that spec's allowlist tolerates only that pattern, and every other spec still fails on any console error
