## ADDED Requirements

### Requirement: Export plugin factory function
The package SHALL export a `pluginGloSharp()` function that returns an Expressive Code plugin object with `preprocessCode`, `annotateCode`, and `postprocessRenderedBlock` hooks.

#### Scenario: Register plugin with Starlight
- **WHEN** `pluginGloSharp()` is added to Starlight's `expressiveCode.plugins` array
- **THEN** the plugin hooks are called during EC's rendering pipeline

### Requirement: Process code in preprocessCode hook
The `preprocessCode` hook SHALL detect C# code blocks with glosharp markers, invoke the CLI via the bridge, store the result, and modify the code to remove marker lines before tokenization.

#### Scenario: Marker removal before tokenization
- **WHEN** a C# code block with `^?` markers enters the EC pipeline
- **THEN** the preprocessCode hook strips markers and the code is tokenized as clean C#

### Requirement: Add hover annotations in annotateCode hook
The `annotateCode` hook SHALL create `GloSharpHoverAnnotation` instances for each hover in the glosharp result, targeting the correct token via `inlineRange`.

#### Scenario: Hover annotation created
- **WHEN** the glosharp result contains a hover at line 0, character 4, length 8
- **THEN** an annotation is added targeting line 0 with `inlineRange` from column 4 to 12

### Requirement: Add error annotations in annotateCode hook
The `annotateCode` hook SHALL create `GloSharpErrorAnnotation` instances for error underlines and error message display.

#### Scenario: Error annotation created
- **WHEN** the glosharp result contains an error at line 3
- **THEN** an inline error underline annotation and a block error message annotation are added for that line

### Requirement: Inject popup HTML in postprocessRenderedBlock
The `postprocessRenderedBlock` hook SHALL inject hover popup HTML containers with CSS anchor positioning into the rendered output.

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
- **WHEN** a page with glosharp-enhanced code blocks is rendered
- **THEN** no `<script>` tags are added by the plugin and popups work via CSS only

### Requirement: Pass-through for non-glosharp code blocks
The plugin SHALL not modify code blocks that don't contain glosharp markers or are not C# language blocks.

#### Scenario: Non-C# code block
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations

#### Scenario: C# without markers
- **WHEN** a C# code block without `^?`, `@errors`, or other glosharp markers enters the pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations
