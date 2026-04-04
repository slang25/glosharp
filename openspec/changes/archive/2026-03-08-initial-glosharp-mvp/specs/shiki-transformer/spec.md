## ADDED Requirements

### Requirement: Export transformer factory function
The package SHALL export a `transformerGloSharp()` function that returns a Shiki transformer object with `preprocess` and `root` hooks.

#### Scenario: Register transformer
- **WHEN** `transformerGloSharp()` is passed in the `transformers` array of `codeToHtml()`
- **THEN** the transformer hooks are called during Shiki's rendering pipeline

### Requirement: Detect glosharp code blocks in preprocess
The `preprocess` hook SHALL detect code blocks with language `csharp` (or `cs`) that contain glosharp markers. Non-glosharp code blocks SHALL be passed through unchanged.

#### Scenario: Code block with markers
- **WHEN** a `csharp` code block contains `^?` markers
- **THEN** the preprocess hook invokes the CLI via the bridge and returns cleaned code

#### Scenario: Code block without markers
- **WHEN** a `csharp` code block has no glosharp markers
- **THEN** the preprocess hook returns the code unchanged

### Requirement: Inject hover popups in root hook
The `root` hook SHALL walk the HAST tree, match token positions to hover data from the glosharp result, and wrap target tokens with hover popup elements using CSS anchor positioning.

#### Scenario: Token with hover data
- **WHEN** a token at line 0, character 4 has associated hover data
- **THEN** the HAST tree contains a `<span class="glosharp-hover">` wrapping the token with `anchor-name: --glosharp-N`, and a sibling `<div class="glosharp-popup">` with `position-anchor: --glosharp-N` containing the formatted hover text

### Requirement: Inject error annotations in root hook
The `root` hook SHALL add error underline elements and error message elements for compiler diagnostics.

#### Scenario: Error at a position
- **WHEN** the glosharp result contains an error at line 3, character 8
- **THEN** the HAST tree contains an error underline span at that position and an error message element with the diagnostic text

### Requirement: CSS anchor positioning for popups
Hover popups SHALL use CSS anchor positioning (`anchor-name`, `position-anchor`, `inset-area: top`) and be shown/hidden via `:hover` CSS pseudo-class. No JavaScript SHALL be required.

#### Scenario: Hover popup visibility
- **WHEN** the rendered HTML is viewed in a browser
- **THEN** hovering over a token with hover data shows a popup positioned above the token using CSS anchoring, with no JS execution

### Requirement: Render structured parts with syntax highlighting
Hover popup content SHALL render the display parts array with appropriate CSS classes for each part kind (e.g., `glosharp-keyword`, `glosharp-className`) to enable syntax-highlighted type information.

#### Scenario: Hover popup content styling
- **WHEN** hover parts include `{kind: "keyword", text: "int"}` and `{kind: "localName", text: "x"}`
- **THEN** the popup HTML contains `<span class="glosharp-keyword">int</span>` and `<span class="glosharp-localName">x</span>`
