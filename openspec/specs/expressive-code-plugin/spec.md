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
The `annotateCode` hook SHALL create `TwohashErrorAnnotation` instances for error underlines and error message display.

#### Scenario: Error annotation created
- **WHEN** the twohash result contains an error at line 3
- **THEN** an inline error underline annotation and a block error message annotation are added for that line

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
The plugin SHALL define `styleSettings` for popup colors (background, foreground, border) and error colors (underline, message background) that adapt to EC's light/dark theme switching.

#### Scenario: Dark theme popup styling
- **WHEN** the EC instance uses a dark theme
- **THEN** popup elements use the dark theme color variables defined in styleSettings

#### Scenario: Light theme popup styling
- **WHEN** the EC instance uses a light theme
- **THEN** popup elements use the light theme color variables

### Requirement: CSS anchor positioning for popups
Popup elements SHALL use CSS anchor positioning and `:hover` for visibility. No runtime JavaScript SHALL be injected.

#### Scenario: Zero JS output
- **WHEN** a page with twohash-enhanced code blocks is rendered
- **THEN** no `<script>` tags are added by the plugin and popups work via CSS only

### Requirement: Pass-through for non-twohash code blocks
The plugin SHALL not modify code blocks that don't contain twohash markers or are not C# language blocks.

#### Scenario: Non-C# code block
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations

#### Scenario: C# without markers
- **WHEN** a C# code block without `^?`, `@errors`, or other twohash markers enters the pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations

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
