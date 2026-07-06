# expressive-code-plugin Delta

## MODIFIED Requirements

### Requirement: Add completion annotations in annotateCode hook
The `annotateCode` hook SHALL create `GloSharpCompletionAnnotation` instances for each completion result, rendering a completion list dropdown below the queried line. The annotation SHALL target the full line (no zero-width inline range) and its render function SHALL return exactly as many nodes as it receives, nesting the completion list inside a wrapper of the line's existing nodes, so that EC core's render-output validation passes.

#### Scenario: Completion annotation created
- **WHEN** the glosharp result contains completions at line 2, character 8
- **THEN** a `GloSharpCompletionAnnotation` is added to line 2 with the completion items

#### Scenario: Completion list rendering
- **WHEN** the annotation renders in the EC pipeline
- **THEN** a styled completion list appears below the code line, showing item labels with kind indicators

#### Scenario: Render is EC-core valid
- **WHEN** a block containing a `^|` completion marker is rendered through the Expressive Code engine
- **THEN** rendering completes without EC core rejecting the annotation output, and the emitted HTML contains the `.glosharp-completion-list`

### Requirement: Add error annotations in annotateCode hook
The `annotateCode` hook SHALL create `GloSharpErrorAnnotation` instances for error underlines and error message display. Annotations SHALL carry the diagnostic severity and apply severity-specific styling: error (red), warning (yellow/amber), info (blue). When a diagnostic spans multiple lines, underline annotations SHALL be created for each affected line. Every error annotation render function — including the message-only annotation placed on the last line of a multi-line span — SHALL return exactly as many nodes as it receives, nesting the message box inside a wrapper node rather than appending it as an extra sibling.

#### Scenario: Error annotation created
- **WHEN** the glosharp result contains an error at line 3
- **THEN** an inline error underline annotation and a block error message annotation are added for that line

#### Scenario: Warning annotation uses amber styling
- **WHEN** the glosharp result contains a warning diagnostic at line 5
- **THEN** the underline annotation uses amber/yellow decoration color and the message annotation uses amber styling

#### Scenario: Multi-line error annotation
- **WHEN** the glosharp result contains a diagnostic spanning lines 2-4
- **THEN** underline annotations are created for lines 2, 3, and 4, and the error message annotation is placed on line 4

#### Scenario: Multi-line message render is EC-core valid
- **WHEN** a block containing a multi-line diagnostic is rendered through the Expressive Code engine
- **THEN** rendering completes without EC core rejecting the annotation output, and the message box appears after the last affected line

### Requirement: Render persistent hovers as always-visible static annotations
For hovers with `persistent: true` (from `^?` markers), the plugin SHALL use a separate annotation class (`GloSharpStaticAnnotation`) that renders as a `<div class="glosharp-noline">` wrapping the line, with a `<div class="glosharp-static">` child containing a `<div class="glosharp-static-container">`. The popup SHALL be always visible without requiring mouse interaction. No arrow caret SHALL be displayed on static containers. Static containers SHALL participate in normal document flow, reserving vertical space, so they never overlap subsequent code lines or other static containers.

#### Scenario: Persistent hover always visible
- **WHEN** a code block contains a `^?` marker targeting token `x`
- **THEN** the hover popup for `x` is rendered in an always-visible state below the code line

#### Scenario: Persistent hover DOM structure
- **WHEN** a persistent hover annotation renders
- **THEN** the line is wrapped in `<div class="glosharp-noline">` containing `<div class="glosharp-static">` with `<div class="glosharp-static-container">`

#### Scenario: Static popups do not overlap content
- **WHEN** a code block contains multiple `^?` markers on consecutive lines
- **THEN** every code line and every static container remains fully visible — no static container's box intersects another code line's or static container's box

## ADDED Requirements

### Requirement: Popup viewport clamping
Hover popups SHALL remain fully within the visual viewport horizontally: popup styles SHALL cap the popup width to the viewport (viewport-aware max-width with content wrapping when constrained), and the positioning logic SHALL clamp the popup's horizontal position so neither edge extends past the viewport, while keeping the popup vertically adjacent to its token.

#### Scenario: Wide popup on a narrow viewport
- **WHEN** a popup whose natural width exceeds 390px opens in a 390px-wide viewport
- **THEN** the popup's bounding box lies fully within the viewport and remains vertically adjacent to its token

#### Scenario: Token near the right edge
- **WHEN** a popup opens for a token near the right edge of the viewport
- **THEN** the popup shifts left as needed so its right edge stays within the viewport
