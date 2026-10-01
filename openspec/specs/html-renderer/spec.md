# html-renderer Specification

## Purpose
Render glosharp results as self-contained HTML (`glosharp render`).
## Requirements
### Requirement: Generate self-contained HTML fragment from GloSharpResult
The `HtmlRenderer` SHALL accept a `GloSharpResult`, classified spans, and a theme, and produce an HTML string containing a `<div class="glosharp-code">` wrapper with an inline `<style>` block, a `<pre><code>` block with syntax-highlighted tokens, hover popup elements, error annotations, and highlight/focus/diff styling.

#### Scenario: Basic HTML output structure
- **WHEN** a `GloSharpResult` with code `var x = 42;` is rendered with theme `github-dark`
- **THEN** the output contains `<div class="glosharp-code" data-theme="github-dark">`, an inline `<style>` block, and a `<pre><code>` block with syntax-highlighted `<span>` elements

#### Scenario: Output is self-contained
- **WHEN** HTML is generated
- **THEN** all CSS needed for syntax highlighting, popups, errors, and highlights is included in the inline `<style>` block — no external CSS files are required

### Requirement: Render hover popups with CSS anchor positioning
The renderer SHALL wrap hover target tokens in `<span class="glosharp-hover">` elements with unique `anchor-name` styles, and SHALL emit each hover's `<div class="glosharp-popup">` **inside** that span, with a matching `position-anchor` style. Popup content SHALL render the hover `parts` array with theme-colored `<span>` elements, followed by documentation text when `docs` is present.

Anchor names SHALL be prefixed per fragment with a short identifier derived from the rendered code (`--gs<8 hex>-<n>`), and the `.glosharp-code` wrapper SHALL declare `anchor-scope: all`. Anchor names are document-global: two fragments on one page that both numbered from `--th-0` would make every popup anchor to the last fragment's token, wherever that is on the page. Deriving the prefix from the code keeps output byte-deterministic, which committed fixtures and content-addressed artifacts both depend on.

#### Scenario: Single hover popup
- **WHEN** a result has one hover at line 0, character 4 with text `(local variable) int x`
- **THEN** the token at that position is wrapped in `<span class="glosharp-hover" style="anchor-name: --gs<id>-0">` and that span contains a `<div class="glosharp-popup" style="position-anchor: --gs<id>-0">` with the parts rendered as themed spans

#### Scenario: Popup is reachable from its anchor
- **WHEN** HTML is rendered
- **THEN** every `.glosharp-popup` is a descendant of the `.glosharp-hover` whose anchor it references, and none appears after `</code></pre>`

#### Scenario: Multiple hovers with unique anchors
- **WHEN** a result has three hovers
- **THEN** their anchor names share the fragment's prefix and are numbered `-0`, `-1`, `-2`, and each popup references its corresponding anchor

#### Scenario: Anchor names differ between fragments
- **WHEN** two different snippets are rendered
- **THEN** their anchor-name prefixes differ, so both fragments can appear on one page without cross-anchoring

#### Scenario: Anchor names are deterministic
- **WHEN** the same result is rendered twice
- **THEN** the two outputs are byte-identical

### Requirement: Render error annotations
The renderer SHALL wrap error spans in `<span class="glosharp-error-underline glosharp-severity-{severity}">` elements, using severity-specific wavy underline colors (red for error, yellow/amber for warning, blue for info). For each error, a `<div class="glosharp-error-message glosharp-severity-{severity}">` SHALL be emitted containing the error message and code. When the error code matches `CS\d+`, the code SHALL be rendered as an `<a>` element linking to Microsoft docs. When a diagnostic spans multiple lines, underline styling SHALL be applied across all affected lines.

#### Scenario: Error underline and message
- **WHEN** a result has an error at line 3 with code `CS1002` and message `; expected`
- **THEN** the affected token is wrapped with `class="glosharp-error-underline glosharp-severity-error"` and a message div contains a linked `CS1002` followed by `; expected`

#### Scenario: Expected errors render the same as unexpected
- **WHEN** an error has `expected: true`
- **THEN** the error is rendered with the same underline and message styling (the `expected` flag does not affect rendering)

#### Scenario: Warning uses amber styling
- **WHEN** a result has a warning diagnostic
- **THEN** the underline uses the theme's warning color and the message div has `glosharp-severity-warning` class

#### Scenario: Info uses blue styling
- **WHEN** a result has an info diagnostic
- **THEN** the underline uses the theme's info color and the message div has `glosharp-severity-info` class

#### Scenario: Multi-line error underline
- **WHEN** a diagnostic spans from line 2, character 8 to line 4, character 12
- **THEN** underline styling is applied on line 2 from character 8 to end, on line 3 fully, and on line 4 from start to character 12

#### Scenario: Error code rendered as link
- **WHEN** a diagnostic has code `CS0246`
- **THEN** the error code in the message div is an `<a>` linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246`

### Requirement: Render highlight, focus, and diff markers
The renderer SHALL apply CSS classes to lines or spans affected by highlight entries. Highlight kinds SHALL map to: `highlight` → `.glosharp-highlight`, `focus` → focus dimming on non-focused lines (`.glosharp-focus-dim`), `add` → `.glosharp-diff-add`, `remove` → `.glosharp-diff-remove`.

#### Scenario: Highlighted line
- **WHEN** a result has a highlight with `kind: "highlight"` on line 2
- **THEN** line 2's `<span class="line">` has the `glosharp-highlight` class applied

#### Scenario: Focus dimming
- **WHEN** a result has focus highlights on lines 3 and 4
- **THEN** lines 3 and 4 are rendered normally, and all other lines have the `glosharp-focus-dim` class applied

#### Scenario: Diff add marker
- **WHEN** a result has a highlight with `kind: "add"` on line 5
- **THEN** line 5 has the `glosharp-diff-add` class applied

#### Scenario: Diff remove marker
- **WHEN** a result has a highlight with `kind: "remove"` on line 6
- **THEN** line 6 has the `glosharp-diff-remove` class applied

### Requirement: Render completion lists
The renderer SHALL render completion entries as `<ul class="glosharp-completion-list">` elements positioned after the relevant line, with each item as an `<li>` containing the label and kind.

#### Scenario: Completion list rendering
- **WHEN** a result has a completion at line 2 with items `[{label: "WriteLine", kind: "Method"}, {label: "Write", kind: "Method"}]`
- **THEN** a `<ul class="glosharp-completion-list">` appears after line 2 with two `<li>` entries showing the method names

### Requirement: CSS popup show/hide via hover
The inline CSS SHALL include rules that show the popup on hover: `.glosharp-hover:hover > .glosharp-popup` and `.glosharp-popup:hover` SHALL set `display: block`. Popups SHALL default to `display: none`. The selector SHALL match the emitted nesting — an adjacent-sibling selector cannot reach a popup that is not a sibling, and a popup that no selector can reach can never be shown.

#### Scenario: Popup hidden by default
- **WHEN** HTML is rendered
- **THEN** the CSS includes `.glosharp-popup { display: none; }` and `.glosharp-hover:hover > .glosharp-popup, .glosharp-popup:hover { display: block; }`

#### Scenario: Hovering a token opens its popup in a browser
- **WHEN** rendered output is loaded in a browser and a hover token is pointed at
- **THEN** that token's popup becomes visible and is positioned adjacent to the token

### Requirement: CSS fallback for older browsers
The inline CSS SHALL include an `@supports not (anchor-name: --x)` block providing absolute positioning fallback for browsers without CSS anchor support.

#### Scenario: Fallback CSS present
- **WHEN** HTML is rendered
- **THEN** the CSS includes `@supports not (anchor-name: --x)` with `.glosharp-hover { position: relative; }` and `.glosharp-popup { position: absolute; bottom: 100%; left: 0; }`

### Requirement: Standalone page mode
The renderer SHALL support a `standalone` flag. When true, the HTML fragment SHALL be wrapped in a full HTML page with `<!DOCTYPE html>`, `<html>`, `<head>` (with charset and viewport meta), and `<body>` elements.

#### Scenario: Standalone output
- **WHEN** rendered with `standalone: true`
- **THEN** output starts with `<!DOCTYPE html>` and includes `<html>`, `<head>`, `<body>` wrapping the code fragment

#### Scenario: Fragment output (default)
- **WHEN** rendered with `standalone: false` or unspecified
- **THEN** output starts with `<div class="glosharp-code">` with no page wrapper

### Requirement: No JavaScript in output
The rendered HTML SHALL NOT contain any `<script>` elements or inline JavaScript. All interactivity SHALL be achieved via CSS.

#### Scenario: No scripts in output
- **WHEN** HTML is rendered with any combination of hovers, errors, and completions
- **THEN** the output contains zero `<script>` tags and no `onclick`, `onmouseover`, or other JS event attributes

### Requirement: Theme includes warning and info colors
The `GloSharpTheme` SHALL include `WarningColor`, `WarningBackground`, `InfoColor`, and `InfoBackground` properties. Built-in themes SHALL define these as: github-dark warning `#d29922`/`rgba(210,153,34,0.15)`, info `#539bf5`/`rgba(83,155,245,0.15)`; github-light warning `#9a6700`/`rgba(154,103,0,0.15)`, info `#0969da`/`rgba(9,105,218,0.15)`.

#### Scenario: Github-dark theme warning colors
- **WHEN** rendering with the `github-dark` theme
- **THEN** warning underlines use `#d29922` and warning message backgrounds use `rgba(210,153,34,0.15)`

#### Scenario: Github-light theme info colors
- **WHEN** rendering with the `github-light` theme
- **THEN** info underlines use `#0969da` and info message backgrounds use `rgba(9,105,218,0.15)`

### Requirement: Code block whitespace is exactly the source's
Line breaks inside the code block SHALL come from the newline characters between line spans and from nothing else: `.glosharp-code .line` SHALL be `display: inline`. Chromium serialises a `display: block` boundary as a newline while Firefox serialises it as nothing, so block-level lines plus real newlines double-space the block and double the newlines Chromium puts on the clipboard, while block-level lines without real newlines copy out of Firefox as a single run-on line. Inline lines plus real newlines is the only combination both browsers lay out and copy correctly.

The renderer SHALL therefore emit exactly the source's newlines inside the code block — in particular a nested popup SHALL NOT be followed by one, or it breaks the line after its hover token.

The cost is accepted: a line-level background (`highlight`, `add`, `remove`) ends with the text rather than spanning the block, matching what the Shiki path already does.

#### Scenario: Newlines match the source
- **WHEN** a result with hovers on several lines is rendered
- **THEN** the markup between `<code>` and `</code></pre>` contains exactly as many newlines as the rendered source

#### Scenario: Lines are single-spaced in a browser
- **WHEN** rendered output is loaded in a browser
- **THEN** the code block's height equals its rendered row count times one line box

#### Scenario: Code copies back out unchanged
- **WHEN** the code block's contents are selected and copied, in Chromium or Firefox
- **THEN** the text is the source lines, one per line, without the hidden popup text

#### Scenario: Line-level styling still applies
- **WHEN** a line carries a highlight or diff class
- **THEN** it still renders its distinguishing background

