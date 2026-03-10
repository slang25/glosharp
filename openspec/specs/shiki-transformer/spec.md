## ADDED Requirements

### Requirement: Detect twohash code blocks in preprocess
The `preprocess` hook SHALL detect code blocks with language `csharp` (or `cs`) that contain twohash markers. Non-twohash code blocks SHALL be passed through unchanged.

#### Scenario: Code block with markers
- **WHEN** a `csharp` code block contains `^?` markers
- **THEN** the preprocess hook invokes the CLI via the bridge and returns cleaned code

#### Scenario: Code block without markers
- **WHEN** a `csharp` code block has no twohash markers
- **THEN** the preprocess hook returns the code unchanged

### Requirement: Inject hover popups in root hook
The `root` hook SHALL walk the HAST tree, match token positions to hover data from the twohash result, and wrap target tokens with hover popup elements using CSS anchor positioning.

#### Scenario: Token with hover data
- **WHEN** a token at line 0, character 4 has associated hover data
- **THEN** the HAST tree contains a `<span class="twohash-hover">` wrapping the token with `anchor-name: --twohash-N`, and a sibling `<div class="twohash-popup">` with `position-anchor: --twohash-N` containing the formatted hover text

### Requirement: Inject error annotations in root hook
The `root` hook SHALL add error underline elements and error message elements for compiler diagnostics. Underline and message elements SHALL include a severity CSS class (`twohash-severity-error`, `twohash-severity-warning`, or `twohash-severity-info`). Error codes matching `CS\d+` SHALL be rendered as `<a>` elements linking to Microsoft docs. When a diagnostic spans multiple lines, underline elements SHALL be applied to each affected line.

#### Scenario: Error at a position
- **WHEN** the twohash result contains an error at line 3, character 8
- **THEN** the HAST tree contains an error underline span at that position and an error message element with the diagnostic text

#### Scenario: Warning with severity class
- **WHEN** the twohash result contains a warning diagnostic
- **THEN** the underline and message elements have class `twohash-severity-warning`

#### Scenario: Error code linked to docs
- **WHEN** the twohash result contains a diagnostic with code `CS0246`
- **THEN** the error code in the message element is an `<a>` linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246`

#### Scenario: Multi-line diagnostic underlines
- **WHEN** the twohash result contains a diagnostic spanning lines 2-4
- **THEN** underline elements are applied to lines 2, 3, and 4, and the error message appears after line 4

### Requirement: CSS anchor positioning for popups
Hover popups SHALL use CSS anchor positioning (`anchor-name`, `position-anchor`, `inset-area: top`) and be shown/hidden via `:hover` CSS pseudo-class. No JavaScript SHALL be required.

#### Scenario: Hover popup visibility
- **WHEN** the rendered HTML is viewed in a browser
- **THEN** hovering over a token with hover data shows a popup positioned above the token using CSS anchoring, with no JS execution

### Requirement: Render structured parts with syntax highlighting
Hover popup content SHALL render the display parts array with appropriate CSS classes for each part kind (e.g., `twohash-keyword`, `twohash-className`) to enable syntax-highlighted type information.

#### Scenario: Hover popup content styling
- **WHEN** hover parts include `{kind: "keyword", text: "int"}` and `{kind: "localName", text: "x"}`
- **THEN** the popup HTML contains `<span class="twohash-keyword">int</span>` and `<span class="twohash-localName">x</span>`

### Requirement: Pass project option to bridge
The `transformerTwohash()` factory SHALL accept a `project` option and pass it through to the twohash bridge when processing code blocks.

#### Scenario: Transformer with project context
- **WHEN** `transformerTwohash({ project: './MyProject.csproj' })` is configured
- **THEN** all twohash CLI invocations include the `--project` argument

#### Scenario: Transformer without project
- **WHEN** `transformerTwohash()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)

### Requirement: Render completion lists in HAST
The `root` hook SHALL inject completion list elements into the HAST tree for each completion result. Completion lists SHALL be rendered as a styled dropdown-like element positioned at the query location.

#### Scenario: Completion list rendered
- **WHEN** the twohash result contains completions at line 2, character 8
- **THEN** the HAST tree contains a `<ul class="twohash-completion-list">` element positioned near the query location, with `<li>` elements for each completion item

#### Scenario: Completion item display
- **WHEN** a completion item has `label: "WriteLine"`, `kind: "Method"`, `detail: "void Console.WriteLine(string?)"`
- **THEN** the rendered `<li>` includes the label text, a kind icon/badge via CSS class `twohash-completion-kind-Method`, and the detail text

#### Scenario: No completions renders nothing
- **WHEN** the twohash result has an empty completions array
- **THEN** no completion list elements are injected into the HAST

### Requirement: Detect completion markers for processing
The marker detection logic SHALL recognize `^|` markers in addition to `^?` markers when deciding whether to invoke twohash processing on a code block.

#### Scenario: Block with only completion markers
- **WHEN** a csharp code block contains `^|` markers but no `^?` markers
- **THEN** the transformer invokes twohash processing on the block
