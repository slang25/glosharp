## ADDED Requirements

### Requirement: Generate self-contained HTML fragment from TwohashResult
The `HtmlRenderer` SHALL accept a `TwohashResult`, classified spans, and a theme, and produce an HTML string containing a `<div class="twohash-code">` wrapper with an inline `<style>` block, a `<pre><code>` block with syntax-highlighted tokens, hover popup elements, error annotations, and highlight/focus/diff styling.

#### Scenario: Basic HTML output structure
- **WHEN** a `TwohashResult` with code `var x = 42;` is rendered with theme `github-dark`
- **THEN** the output contains `<div class="twohash-code" data-theme="github-dark">`, an inline `<style>` block, and a `<pre><code>` block with syntax-highlighted `<span>` elements

#### Scenario: Output is self-contained
- **WHEN** HTML is generated
- **THEN** all CSS needed for syntax highlighting, popups, errors, and highlights is included in the inline `<style>` block — no external CSS files are required

### Requirement: Render hover popups with CSS anchor positioning
The renderer SHALL wrap hover target tokens in `<span class="twohash-hover">` elements with unique `anchor-name` styles (`--th-0`, `--th-1`, etc.). For each hover, a sibling `<div class="twohash-popup">` element SHALL be emitted with matching `position-anchor` style. Popup content SHALL render the hover `parts` array with theme-colored `<span>` elements, followed by documentation text when `docs` is present.

#### Scenario: Single hover popup
- **WHEN** a result has one hover at line 0, character 4 with text `(local variable) int x`
- **THEN** the token at that position is wrapped in `<span class="twohash-hover" style="anchor-name: --th-0">` and a `<div class="twohash-popup" style="position-anchor: --th-0">` follows with the parts rendered as themed spans

#### Scenario: Hover popup with documentation
- **WHEN** a hover has a `docs` object with `summary: "Gets the value."`
- **THEN** the popup contains the type signature parts followed by a `<div class="twohash-popup-docs">Gets the value.</div>`

#### Scenario: Multiple hovers with unique anchors
- **WHEN** a result has three hovers
- **THEN** anchor names are `--th-0`, `--th-1`, `--th-2` and each popup references its corresponding anchor

### Requirement: Render error annotations
The renderer SHALL wrap error spans in `<span class="twohash-error-underline">` elements. For each error, a `<div class="twohash-error-message">` SHALL be emitted containing the error message and code. Error styling SHALL use wavy underline decoration.

#### Scenario: Error underline and message
- **WHEN** a result has an error at line 3 with code `CS1002` and message `; expected`
- **THEN** the affected token is wrapped with `class="twohash-error-underline"` and a message div contains `; expected [CS1002]`

#### Scenario: Expected errors render the same as unexpected
- **WHEN** an error has `expected: true`
- **THEN** the error is rendered with the same underline and message styling (the `expected` flag does not affect rendering)

### Requirement: Render highlight, focus, and diff markers
The renderer SHALL apply CSS classes to lines or spans affected by highlight entries. Highlight kinds SHALL map to: `highlight` → `.twohash-highlight`, `focus` → focus dimming on non-focused lines (`.twohash-focus-dim`), `add` → `.twohash-diff-add`, `remove` → `.twohash-diff-remove`.

#### Scenario: Highlighted line
- **WHEN** a result has a highlight with `kind: "highlight"` on line 2
- **THEN** line 2's `<span class="line">` has the `twohash-highlight` class applied

#### Scenario: Focus dimming
- **WHEN** a result has focus highlights on lines 3 and 4
- **THEN** lines 3 and 4 are rendered normally, and all other lines have the `twohash-focus-dim` class applied

#### Scenario: Diff add marker
- **WHEN** a result has a highlight with `kind: "add"` on line 5
- **THEN** line 5 has the `twohash-diff-add` class applied

#### Scenario: Diff remove marker
- **WHEN** a result has a highlight with `kind: "remove"` on line 6
- **THEN** line 6 has the `twohash-diff-remove` class applied

### Requirement: Render completion lists
The renderer SHALL render completion entries as `<ul class="twohash-completion-list">` elements positioned after the relevant line, with each item as an `<li>` containing the label and kind.

#### Scenario: Completion list rendering
- **WHEN** a result has a completion at line 2 with items `[{label: "WriteLine", kind: "Method"}, {label: "Write", kind: "Method"}]`
- **THEN** a `<ul class="twohash-completion-list">` appears after line 2 with two `<li>` entries showing the method names

### Requirement: CSS popup show/hide via hover
The inline CSS SHALL include rules that show the popup on hover: `.twohash-hover:hover + .twohash-popup` and `.twohash-popup:hover` SHALL set `display: block`. Popups SHALL default to `display: none`.

#### Scenario: Popup hidden by default
- **WHEN** HTML is rendered
- **THEN** the CSS includes `.twohash-popup { display: none; }` and `.twohash-hover:hover + .twohash-popup, .twohash-popup:hover { display: block; }`

### Requirement: CSS fallback for older browsers
The inline CSS SHALL include an `@supports not (anchor-name: --x)` block providing absolute positioning fallback for browsers without CSS anchor support.

#### Scenario: Fallback CSS present
- **WHEN** HTML is rendered
- **THEN** the CSS includes `@supports not (anchor-name: --x)` with `.twohash-hover { position: relative; }` and `.twohash-popup { position: absolute; bottom: 100%; left: 0; }`

### Requirement: Standalone page mode
The renderer SHALL support a `standalone` flag. When true, the HTML fragment SHALL be wrapped in a full HTML page with `<!DOCTYPE html>`, `<html>`, `<head>` (with charset and viewport meta), and `<body>` elements.

#### Scenario: Standalone output
- **WHEN** rendered with `standalone: true`
- **THEN** output starts with `<!DOCTYPE html>` and includes `<html>`, `<head>`, `<body>` wrapping the code fragment

#### Scenario: Fragment output (default)
- **WHEN** rendered with `standalone: false` or unspecified
- **THEN** output starts with `<div class="twohash-code">` with no page wrapper

### Requirement: No JavaScript in output
The rendered HTML SHALL NOT contain any `<script>` elements or inline JavaScript. All interactivity SHALL be achieved via CSS.

#### Scenario: No scripts in output
- **WHEN** HTML is rendered with any combination of hovers, errors, and completions
- **THEN** the output contains zero `<script>` tags and no `onclick`, `onmouseover`, or other JS event attributes
