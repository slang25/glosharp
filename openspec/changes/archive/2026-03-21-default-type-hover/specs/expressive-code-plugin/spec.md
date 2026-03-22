## ADDED Requirements

### Requirement: Process all C# code blocks
The plugin SHALL invoke twohash processing on ALL C# code blocks, regardless of whether they contain `^?`, `@errors`, or other twohash markers. Non-C# code blocks SHALL continue to be skipped.

#### Scenario: C# block without markers is processed
- **WHEN** a C# code block contains `var x = 42;` with no twohash markers
- **THEN** the plugin invokes twohash processing and produces auto-hover annotations

#### Scenario: Non-C# block still skipped
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke twohash processing

### Requirement: Render default hovers as mouse-over popups
For hovers with `persistent: false`, the plugin SHALL render a `<span class="twohash-hover">` wrapper around the token. The popup SHALL only be visible on `:hover` interaction. The token SHALL NOT have any visible underline or decoration in its default state — it should appear as normal code until hovered.

#### Scenario: Default hover invisible until interaction
- **WHEN** a code block is rendered with auto-hover data for token `x`
- **THEN** the token `x` appears as normal code with no visual decoration, and hovering reveals the type popup

#### Scenario: Default hover popup content
- **WHEN** a user hovers over a token with auto-hover data
- **THEN** the popup displays the same structured content as today (type signature, display parts, docs)

### Requirement: Render persistent hovers as always-visible popups
For hovers with `persistent: true` (from `^?` markers), the plugin SHALL render a `<span class="twohash-hover twohash-hover-persistent">` wrapper. The popup SHALL be always visible without requiring mouse interaction. The token SHALL have a visible underline decoration to indicate the pinned annotation.

#### Scenario: Persistent hover always visible
- **WHEN** a code block contains a `^?` marker targeting token `x`
- **THEN** the hover popup for `x` is rendered in an always-visible state (no `:hover` gate)

#### Scenario: Persistent hover token styling
- **WHEN** a code block renders a persistent hover
- **THEN** the targeted token has a visible underline decoration distinguishing it from default-hover tokens

#### Scenario: Persistent hover CSS class
- **WHEN** a persistent hover annotation renders
- **THEN** the wrapper element has both `twohash-hover` and `twohash-hover-persistent` CSS classes

## MODIFIED Requirements

### Requirement: Pass-through for non-twohash code blocks
The plugin SHALL not modify code blocks that are not C# language blocks. C# code blocks SHALL always be processed for auto-hover extraction regardless of marker presence.

#### Scenario: Non-C# code block
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations

#### Scenario: C# without markers still processed
- **WHEN** a C# code block without `^?`, `@errors`, or other twohash markers enters the pipeline
- **THEN** the plugin invokes twohash processing and adds auto-hover annotations for all semantically meaningful tokens
