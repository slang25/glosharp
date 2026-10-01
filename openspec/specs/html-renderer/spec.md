# html-renderer Specification

## Purpose
Render glosharp results as self-contained HTML (`glosharp render`).
## Requirements
### Requirement: Generate self-contained HTML fragment from GloSharpResult
The `HtmlRenderer` SHALL accept a `GloSharpResult`, classified spans, and a theme, and produce an HTML string containing a `<div class="glosharp-code" data-theme="{theme}">` wrapper with a `<style>` block, a `<pre><code>` block with syntax-highlighted tokens, hover popups, diagnostics, persistent query results, custom tag callouts, completion lists, and highlight/focus/diff styling.

#### Scenario: Basic HTML output structure
- **WHEN** a `GloSharpResult` with code `var x = 42;` is rendered with theme `github-dark`
- **THEN** the output starts with `<div class="glosharp-code" data-theme="github-dark">`, contains a `<style>` block, and a `<pre><code>` block with syntax-highlighted `<span>` elements

#### Scenario: Output is self-contained
- **WHEN** HTML is generated
- **THEN** all CSS needed for syntax highlighting, popups, diagnostics, callouts, and highlights is included in the `<style>` block — no external CSS files are required

#### Scenario: Stylesheet can be emitted once per page
- **WHEN** rendered with `IncludeStyles = false`
- **THEN** the fragment contains no `<style>` element, and `HtmlRenderer.GenerateStylesheet(theme)` returns the CSS to include once on the page

#### Scenario: No trailing empty line
- **WHEN** the code ends with a newline
- **THEN** no empty `<span class="line"></span>` is emitted for the empty final line

### Requirement: Stylesheet is scoped to its fragment
Every selector in the generated CSS SHALL be prefixed with `.glosharp-code[data-theme="{theme}"]`, so fragments in different themes, and output from the Shiki and Expressive Code renderers, can share a page without restyling each other. The CSS SHALL contain no comments.

#### Scenario: Two themes on one page
- **WHEN** a `github-dark` and a `github-light` fragment are placed on the same page
- **THEN** each code block keeps its own theme's background, popup, and diagnostic colors

#### Scenario: Foreign glosharp classes are untouched
- **WHEN** a page contains an element with class `glosharp-completion-list` outside any fragment
- **THEN** the fragment's CSS does not apply to it

### Requirement: Render hover popups with CSS anchor positioning
The renderer SHALL wrap each hover target in `<span class="glosharp-hover" tabindex="0" aria-describedby="{id}" style="anchor-name:--{id}">`. The popup SHALL be a `<span class="glosharp-popup" role="tooltip" id="{id}" style="position-anchor:--{id}">` nested inside its hover span. `{id}` is derived from a hash of the theme name and the code, so it is unique per fragment and deterministic. Persistent (`^?`) hovers additionally carry the `glosharp-hover-persistent` class.

Popup content SHALL render the hover `parts` with theme-colored spans, then `(+N overloads)` when `overloadCount` is greater than 1, then a type-annotations section when `typeAnnotations` is present, then a docs section with every populated field: summary, parameters, returns, remarks, examples, and exceptions.

#### Scenario: Single hover popup
- **WHEN** a result has one hover at line 0, character 4 with text `(local variable) int x`
- **THEN** the token at that position is wrapped in a `glosharp-hover` span, and a `glosharp-popup` span with matching `position-anchor` is nested inside it, containing the parts as themed spans

#### Scenario: Hover popup with full documentation
- **WHEN** a hover has `docs` with summary, params, returns, remarks, examples and exceptions
- **THEN** the popup contains `glosharp-popup-summary`, `glosharp-popup-params`, `glosharp-popup-returns`, `glosharp-popup-remarks`, `glosharp-popup-example`, and `glosharp-popup-exceptions` sections

#### Scenario: Overload count
- **WHEN** a hover has `overloadCount: 20`
- **THEN** the popup signature ends with `(+19 overloads)`

#### Scenario: Anchor names are unique per snippet
- **WHEN** two fragments with different code, or the same code in different themes, are on one page
- **THEN** their anchor names and popup ids differ

### Requirement: Popups are keyboard accessible
Hover targets SHALL be focusable (`tabindex="0"`), show a visible focus ring on `:focus-visible`, and reveal their popup on `:focus-visible` as well as `:hover`. Each hover target SHALL reference its popup via `aria-describedby`, and the popup SHALL have `role="tooltip"`. Dismissing a popup with Escape requires script and is left to hosts that run script (the fragment stays script-free).

#### Scenario: Keyboard focus opens the popup
- **WHEN** a hover target receives keyboard focus
- **THEN** its popup is displayed and the target has a visible outline

### Requirement: Popup placement
Popups SHALL open below their token, aligned to its left edge (`position-area: bottom span-right`), and SHALL fall back to `bottom span-left`, `bottom span-all`, then the `top` equivalents when there is no room, so a popup never covers the token it describes. Signature and docs sections SHALL each have a `max-height` with scrolling, and the popup SHALL never be wider than the viewport. Popups SHALL hide when their token is scrolled out of view (`position-visibility: anchors-visible`).

#### Scenario: Token near the top of the viewport
- **WHEN** a token near the top of the viewport is hovered
- **THEN** the popup opens below it and does not overlap it

#### Scenario: Token near the bottom of the viewport
- **WHEN** a token near the bottom of the viewport is hovered and the popup does not fit below
- **THEN** the popup opens above it and does not overlap it

### Requirement: Render diagnostics
The renderer SHALL render diagnostics as a layer independent of tokens and hovers: every character in `[character, character + length)` is wrapped in `<span class="glosharp-error-underline glosharp-severity-{severity}">`, splitting across tokens and nesting inside hover spans as needed. The underline SHALL be a wavy `text-decoration` in the severity color. For each diagnostic, a `<span class="glosharp-callout glosharp-error-message glosharp-severity-{severity}" role="note">` SHALL be rendered inside the code block, directly after the diagnostic's last line, in the code font, with a severity icon. When the code matches `CS\d+`, it SHALL be an `<a>` linking to Microsoft docs. Diagnostics with severity `hidden` SHALL NOT be rendered.

#### Scenario: Underline covers the full range
- **WHEN** an error starts mid-token and spans several tokens
- **THEN** exactly the characters in its range are underlined

#### Scenario: Zero-width diagnostic at end of line
- **WHEN** a `CS1002` `; expected` diagnostic has length 0 at the end of `int x = 1`
- **THEN** the preceding token `1` is underlined (at the start of a line, the following token is underlined instead)

#### Scenario: Diagnostic on a blank line
- **WHEN** a diagnostic is reported on a line with no text
- **THEN** a `glosharp-error-marker` span draws a short squiggle from generated content

#### Scenario: Warning on a hovered identifier
- **WHEN** an identifier has both a hover and a warning
- **THEN** it keeps its hover popup and also shows the warning squiggle

#### Scenario: Multi-line diagnostic
- **WHEN** a diagnostic spans from line 2, character 8 to line 4, character 12
- **THEN** line 2 is underlined from character 8 to its end, line 3 from its indentation to its end, line 4 from its indentation to character 12, and the message appears once, after line 4

#### Scenario: Expected errors
- **WHEN** an error has `expected: true`
- **THEN** it renders like any other error, with an additional `glosharp-error-expected` class on its message for consumers who want to style it

#### Scenario: Error code rendered as link
- **WHEN** a diagnostic has code `CS0246`
- **THEN** the error code in the message is an `<a>` linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246`

### Requirement: Render persistent query results
For each hover with `persistent: true` (a `^?` query), the renderer SHALL render an always-visible `<span class="glosharp-callout glosharp-static">` after the hover's line, indented to the hover's column (capped so the callout keeps at least 34 characters of width), containing a `glosharp-static-container` with the signature, overload count, and summary.

#### Scenario: Persistent query
- **WHEN** a result has a persistent hover at line 0, character 4
- **THEN** a `glosharp-static` callout with `--glosharp-column:4ch` follows line 0 and shows the hover's signature without hovering

### Requirement: Render custom tags
For each entry in `tags`, the renderer SHALL render a `<span class="glosharp-callout glosharp-tag glosharp-tag-{name}" role="note">` after the tag's line, with an icon, the tag name as a title, and the tag text. `log`, `warn` and `error` use the theme's info, warning and error colors; `annotate` uses the theme's annotate color.

#### Scenario: Custom tag callouts
- **WHEN** a result has `@log`, `@warn`, `@error` and `@annotate` tags
- **THEN** four callouts with classes `glosharp-tag-log`, `glosharp-tag-warn`, `glosharp-tag-error` and `glosharp-tag-annotate` appear after their lines

### Requirement: Callouts stay out of copied code
Callouts (`glosharp-callout`: diagnostics, persistent queries, tags, completions) SHALL be block-level rows inside the code block that add no newline to the code and SHALL be excluded from text selection (`user-select: none`), so selecting and copying the block yields exactly the source.

#### Scenario: Copying a block with callouts
- **WHEN** a code block with persistent queries is selected and copied
- **THEN** the copied text equals the processed source

### Requirement: Render highlight, focus, and diff markers
The renderer SHALL apply CSS classes to lines affected by highlight entries. Highlight kinds SHALL map to: `highlight` → `.glosharp-highlight`, `focus` → focus dimming on non-focused lines (`.glosharp-focus-dim`, undimmed while the block is hovered), `add` → `.glosharp-diff-add`, `remove` → `.glosharp-diff-remove`. Diff markers SHALL NOT shift the line's text.

#### Scenario: Highlighted line
- **WHEN** a result has a highlight with `kind: "highlight"` on line 2
- **THEN** line 2's `<span class="line">` has the `glosharp-highlight` class applied

#### Scenario: Focus dimming
- **WHEN** a result has focus highlights on lines 3 and 4
- **THEN** lines 3 and 4 are rendered normally, and all other lines have the `glosharp-focus-dim` class applied

#### Scenario: Diff markers
- **WHEN** a result has highlights with `kind: "add"` on line 5 and `kind: "remove"` on line 6
- **THEN** line 5 has the `glosharp-diff-add` class and line 6 the `glosharp-diff-remove` class

### Requirement: Render completion lists
The renderer SHALL render each completion as a `<span class="glosharp-callout glosharp-completion">` after its line, indented to the completion's column, containing a focusable, scrollable `glosharp-completion-list` with one `glosharp-completion-item` per item (kind, label, and detail when present). An empty completion SHALL render `No completions` rather than an empty box.

#### Scenario: Completion list rendering
- **WHEN** a result has a completion at line 2 with items `[{label: "WriteLine", kind: "Method"}, {label: "Write", kind: "Method"}]`
- **THEN** a `glosharp-completion-list` appears after line 2 with two items showing the method names

#### Scenario: Empty completion
- **WHEN** a completion has no items
- **THEN** the list shows `No completions`

### Requirement: Valid markup
Everything inside `<pre><code>` SHALL be phrasing content (`<span>`, `<code>`, `<a>`, `<svg>`); block-styled elements are `<span>`s with `display: block`. The standalone page SHALL place its `<style>` in `<head>`. The fragment form necessarily carries its `<style>` inside the wrapper `<div>`, which HTML does not permit; use `IncludeStyles = false` with a page-level stylesheet for strictly valid markup.

#### Scenario: Standalone page validates
- **WHEN** a standalone page is checked with `html-validate` (with `no-inline-style` disabled — anchor names must be inline)
- **THEN** it reports no errors

### Requirement: CSS fallback for older browsers
The CSS SHALL include an `@supports not (anchor-name: --x)` block that positions popups absolutely below their hover span.

#### Scenario: Fallback CSS present
- **WHEN** HTML is rendered
- **THEN** the CSS includes `@supports not (anchor-name: --x)` with a relatively positioned `.glosharp-hover` and an absolutely positioned `.glosharp-popup` at `top: 100%; left: 0`

### Requirement: Standalone page mode
The renderer SHALL support a `Standalone` flag. When true, the HTML fragment SHALL be wrapped in a full HTML page with `<!DOCTYPE html>`, `<html lang="en">`, `<head>` (with charset and viewport meta, a `<title>` from `HtmlRenderOptions.Title` defaulting to `glosharp`, and the stylesheet), and `<body>`, with the page background matching the theme.

#### Scenario: Standalone output
- **WHEN** rendered with `Standalone = true`
- **THEN** output starts with `<!DOCTYPE html>` and includes `<html>`, `<head>`, `<body>` wrapping the code fragment

#### Scenario: Fragment output (default)
- **WHEN** rendered with `Standalone = false` or unspecified
- **THEN** output starts with `<div class="glosharp-code"` with no page wrapper

### Requirement: No JavaScript in output
The rendered HTML SHALL NOT contain any `<script>` elements or inline JavaScript. All interactivity SHALL be achieved via CSS.

#### Scenario: No scripts in output
- **WHEN** HTML is rendered with any combination of hovers, errors, and completions
- **THEN** the output contains zero `<script>` tags and no `onclick`, `onmouseover`, or other JS event attributes

### Requirement: Theme includes warning, info and annotate colors
The `GloSharpTheme` SHALL include `WarningColor`, `WarningBackground`, `InfoColor`, `InfoBackground`, `AnnotateColor`, `AnnotateBackground` and `ColorScheme` properties. Built-in themes SHALL define these as: github-dark warning `#d29922`/`rgba(210,153,34,0.15)`, info `#539bf5`/`rgba(83,155,245,0.15)`, annotate `#bc8cff`, color scheme `dark`; github-light warning `#9a6700`/`rgba(154,103,0,0.15)`, info `#0969da`/`rgba(9,105,218,0.15)`, annotate `#8250df`, color scheme `light`.

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

