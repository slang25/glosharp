## ADDED Requirements

### Requirement: Export plugin factory function
The package SHALL export a `pluginTwohash()` function that returns an Expressive Code plugin object with `preprocessCode`, `annotateCode`, and `postprocessRenderedBlock` hooks.

#### Scenario: Register plugin with Starlight
- **WHEN** `pluginTwohash()` is added to Starlight's `expressiveCode.plugins` array
- **THEN** the plugin hooks are called during EC's rendering pipeline

### Requirement: Process code in preprocessCode hook
The `preprocessCode` hook SHALL detect C# code blocks with twohash markers, invoke the CLI via the bridge, store the result, and modify the code to remove marker lines before tokenization.

#### Scenario: Marker removal before tokenization
- **WHEN** a C# code block with `^?` markers enters the EC pipeline
- **THEN** the preprocessCode hook strips markers and the code is tokenized as clean C#

### Requirement: Add hover annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashHoverAnnotation` instances for each hover in the twohash result, targeting the correct token via `inlineRange`.

#### Scenario: Hover annotation created
- **WHEN** the twohash result contains a hover at line 0, character 4, length 8
- **THEN** an annotation is added targeting line 0 with `inlineRange` from column 4 to 12

### Requirement: Add error annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashErrorAnnotation` instances for error underlines and error message display. Annotations SHALL carry the diagnostic severity and apply severity-specific styling: error (red), warning (yellow/amber), info (blue). When a diagnostic spans multiple lines, underline annotations SHALL be created for each affected line.

#### Scenario: Error annotation created
- **WHEN** the twohash result contains an error at line 3
- **THEN** an inline error underline annotation and a block error message annotation are added for that line

#### Scenario: Warning annotation uses amber styling
- **WHEN** the twohash result contains a warning diagnostic at line 5
- **THEN** the underline annotation uses amber/yellow decoration color and the message annotation uses amber styling

#### Scenario: Multi-line error annotation
- **WHEN** the twohash result contains a diagnostic spanning lines 2-4
- **THEN** underline annotations are created for lines 2, 3, and 4, and the error message annotation is placed on line 4

### Requirement: Inject popup HTML in postprocessRenderedBlock
The `postprocessRenderedBlock` hook SHALL inject hover popup HTML containers with CSS anchor positioning into the rendered output. Popup content SHALL include structured doc sections when available: summary text, a parameter list, return description, remarks, examples, and exception list. Each section SHALL be rendered in a distinct styled container.

#### Scenario: Popup with summary only
- **WHEN** a hover has `docs` with only `summary` populated
- **THEN** the popup renders the summary in a `.twohash-popup-docs` div, visually identical to the current behavior

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

### Requirement: Theme-aware styling via styleSettings
The plugin SHALL define theme-aware styles for popup colors (background, foreground, border), error colors (underline, message background), warning colors (underline, message background), and info colors (underline, message background) that adapt to EC's light/dark theme switching. These styles SHALL be embedded in `baseStyles` using CSS custom properties. The plugin SHALL NOT return a `styleSettings` property on the plugin object, as `baseStyles` is the sole mechanism for theme-aware styling.

#### Scenario: Dark theme popup styling
- **WHEN** the EC instance uses a dark theme
- **THEN** popup elements use the dark theme color variables defined in baseStyles

#### Scenario: Light theme popup styling
- **WHEN** the EC instance uses a light theme
- **THEN** popup elements use the light theme color variables

#### Scenario: Plugin object has no styleSettings property
- **WHEN** `pluginTwohash()` is called
- **THEN** the returned plugin object does not contain a `styleSettings` property

#### Scenario: Plugin works with EC 0.41 without workaround
- **WHEN** `pluginTwohash()` is added directly to an expressive-code `plugins` array in EC 0.41+
- **THEN** the plugin registers without errors and no consumer-side property stripping is needed

### Requirement: CSS anchor positioning for popups
Popup elements SHALL use CSS anchor positioning and `:hover` for visibility. No runtime JavaScript SHALL be injected.

#### Scenario: Zero JS output
- **WHEN** a page with twohash-enhanced code blocks is rendered
- **THEN** no `<script>` tags are added by the plugin and popups work via CSS only

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

### Requirement: Pass-through for non-twohash code blocks
The plugin SHALL not modify code blocks that are not C# language blocks. C# code blocks SHALL always be processed for auto-hover extraction regardless of marker presence.

#### Scenario: Non-C# code block
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations

#### Scenario: C# without markers still processed
- **WHEN** a C# code block without `^?`, `@errors`, or other twohash markers enters the pipeline
- **THEN** the plugin invokes twohash processing and adds auto-hover annotations for all semantically meaningful tokens

### Requirement: Pass project option to bridge
The `pluginTwohash()` factory SHALL accept a `project` option and pass it through to the twohash bridge when processing code blocks.

#### Scenario: Plugin with project context
- **WHEN** `pluginTwohash({ project: './MyProject.csproj' })` is configured
- **THEN** all twohash CLI invocations include the `--project` argument

#### Scenario: Plugin without project
- **WHEN** `pluginTwohash()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)

### Requirement: Add completion annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashCompletionAnnotation` instances for each completion result, rendering a completion list dropdown below the queried line.

#### Scenario: Completion annotation created
- **WHEN** the twohash result contains completions at line 2, character 8
- **THEN** a `TwohashCompletionAnnotation` is added to line 2 with the completion items

#### Scenario: Completion list rendering
- **WHEN** the annotation renders in the EC pipeline
- **THEN** a styled completion list appears below the code line, showing item labels with kind indicators

### Requirement: Detect completion markers for processing
The marker detection logic SHALL recognize `^|` markers in addition to `^?` markers when deciding whether to invoke twohash processing on a code block.

#### Scenario: Block with only completion markers
- **WHEN** a C# code block contains `^|` markers but no `^?` markers
- **THEN** the plugin invokes twohash processing on the block

### Requirement: Theme-aware completion list styling
The completion list SHALL use `styleSettings` for colors that adapt to the EC theme, consistent with the existing popup styling.

#### Scenario: Completion list in dark theme
- **WHEN** the EC instance uses a dark theme
- **THEN** the completion list uses dark theme background and foreground colors from styleSettings

### Requirement: Theme-aware styling for doc sections
The plugin SHALL define CSS classes for each doc section (`.twohash-popup-params`, `.twohash-popup-returns`, `.twohash-popup-remarks`, `.twohash-popup-example`, `.twohash-popup-exceptions`) with styles consistent with the existing popup design. Parameter names SHALL be visually distinct (e.g., monospace or bold).

#### Scenario: Param list styling
- **WHEN** a popup with params is rendered
- **THEN** each parameter is displayed with its name in a distinct style (code font) followed by its description

#### Scenario: Section separators
- **WHEN** a popup has multiple doc sections
- **THEN** each section is visually separated (consistent with the existing `.twohash-popup-docs` border-top pattern)

### Requirement: Detect directive markers for processing
The marker detection logic SHALL recognize `@highlight`, `@focus`, and `@diff` markers in addition to existing markers when deciding whether to invoke twohash processing on a code block.

#### Scenario: Block with only highlight markers
- **WHEN** a C# code block contains `// @highlight` but no `^?` or `@errors` markers
- **THEN** the plugin invokes twohash processing on the block

#### Scenario: Block with only diff markers
- **WHEN** a C# code block contains `// @diff: +` but no other twohash markers
- **THEN** the plugin invokes twohash processing on the block

### Requirement: Add highlight annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashHighlightAnnotation` instances for each highlight entry with `kind: "highlight"`, applying a background color to the entire line.

#### Scenario: Highlight annotation created
- **WHEN** the twohash result contains a highlight with `kind: "highlight"` at line 2
- **THEN** a line-level annotation is added to line 2 that renders a highlight background

#### Scenario: Highlight annotation rendering
- **WHEN** the annotation renders in the EC pipeline
- **THEN** the line has a visible background color distinguishing it from non-highlighted lines

### Requirement: Add focus annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashFocusAnnotation` instances for focus presentation. Lines with `kind: "focus"` SHALL remain at full opacity. All other lines in the code block SHALL be dimmed when any focus entries exist.

#### Scenario: Focus annotation dims non-focused lines
- **WHEN** the twohash result contains focus entries for lines 2 and 3 in a 5-line block
- **THEN** lines 0, 1, and 4 are rendered with reduced opacity, while lines 2 and 3 remain at full opacity

#### Scenario: No focus entries means no dimming
- **WHEN** the twohash result contains no focus entries
- **THEN** all lines render at full opacity (no dimming applied)

### Requirement: Add diff annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashDiffAnnotation` instances for diff presentation. Lines with `kind: "add"` SHALL have a green-tinted background. Lines with `kind: "remove"` SHALL have a red-tinted background.

#### Scenario: Diff add annotation rendering
- **WHEN** the twohash result contains a highlight with `kind: "add"` at line 3
- **THEN** line 3 is rendered with a green-tinted background color

#### Scenario: Diff remove annotation rendering
- **WHEN** the twohash result contains a highlight with `kind: "remove"` at line 4
- **THEN** line 4 is rendered with a red-tinted background color

### Requirement: Theme-aware styling for highlight, focus, and diff
The plugin SHALL define `styleSettings` for highlight background, focus dimmed opacity, diff add background, and diff remove background colors that adapt to EC's light/dark theme switching.

#### Scenario: Highlight in dark theme
- **WHEN** the EC instance uses a dark theme
- **THEN** highlighted lines use a dark-appropriate background color

#### Scenario: Diff colors in light theme
- **WHEN** the EC instance uses a light theme
- **THEN** diff add lines use a light green background and diff remove lines use a light red background

### Requirement: Render clickable error codes in error messages
The `postprocessRenderedBlock` hook SHALL render error codes matching `CS\d+` as `<a>` elements linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{code}`. Links SHALL open in a new tab with `rel="noopener"`. Non-CS codes SHALL remain plain text.

#### Scenario: CS error code linked
- **WHEN** an error message with code `CS1002` is rendered in the EC pipeline
- **THEN** the error code is an `<a>` element with href to the Microsoft docs page and `target="_blank"`

#### Scenario: Analyzer code not linked
- **WHEN** an error message with code `CA1234` is rendered
- **THEN** the error code is plain text without a link
