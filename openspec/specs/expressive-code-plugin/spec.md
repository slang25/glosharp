# expressive-code-plugin Specification

## Purpose
The `@glosharp/expressive-code` plugin that renders glosharp results in Expressive Code.

## Requirements
### Requirement: Export plugin factory function
The package SHALL export a `pluginGloSharp()` function that returns an Expressive Code plugin object (typed as EC's `ExpressiveCodePlugin`, built with `definePlugin`) with `preprocessCode` and `annotateCode` hooks, a `PluginStyleSettings` instance as `styleSettings`, `baseStyles`, and a `jsModules` entry. The returned object SHALL type-check wherever EC accepts a plugin (e.g. a `// @ts-check` `ec.config.mjs` using `defineEcConfig`).

#### Scenario: Register plugin with Starlight
- **WHEN** `pluginGloSharp()` is added to Starlight's `expressiveCode.plugins` array
- **THEN** the plugin hooks are called during EC's rendering pipeline

#### Scenario: Typed EC config
- **WHEN** `astro check` runs on a typed `ec.config.mjs` containing `defineEcConfig({ plugins: [pluginGloSharp()] })`
- **THEN** no type error is reported for the plugin

### Requirement: Process code in preprocessCode hook
The `preprocessCode` hook SHALL invoke the CLI via the bridge for every block selected for processing, store the result, and update the block's code to the processed code before tokenization. Lines that survive processing SHALL keep their existing `ExpressiveCodeLine` objects: only removed lines (markers, directives, cut sections) SHALL be deleted, and any line without a counterpart SHALL be inserted. Other plugins' line-bound metadata (EC line markers such as `{1-3}`, `ins={…}`, `del={…}`, and `collapse={…}` sections) therefore keeps applying, with line ranges counted on the source as written. Trailing carriage returns SHALL be removed from line text.

#### Scenario: Marker removal before tokenization
- **WHEN** a C# code block with `^?` markers enters the EC pipeline
- **THEN** the preprocessCode hook strips markers and the code is tokenized as clean C#

#### Scenario: Line markers survive marker removal
- **WHEN** a block with meta `{3}` has a `^?` marker on source line 2
- **THEN** the rendered line holding source line 3 carries EC's `mark` styling

#### Scenario: Collapsible sections survive marker removal
- **WHEN** a block with `collapse={5-6}` is processed and `@expressive-code/plugin-collapsible-sections` is registered
- **THEN** the rendered block contains one collapsible section holding source lines 5 and 6

### Requirement: Add hover annotations in annotateCode hook
The `annotateCode` hook SHALL create `GloSharpHoverAnnotation` instances for each non-persistent hover in the glosharp result, targeting the correct token via `inlineRange`. The first hover token of a block in document order SHALL be rendered with `tabindex="0"` and every other hover token with `tabindex="-1"` (roving tab stop).

#### Scenario: Hover annotation created
- **WHEN** the glosharp result contains a hover at line 0, character 4, length 8
- **THEN** an annotation is added targeting line 0 with `inlineRange` from column 4 to 12

#### Scenario: One tab stop per block
- **WHEN** a block with three hover tokens is rendered
- **THEN** the first token in document order has `tabindex="0"` and the others `tabindex="-1"`

### Requirement: Add error annotations in annotateCode hook
The `annotateCode` hook SHALL render every diagnostic in the result's `errors` (except severity `hidden`), including diagnostics marked `expected` via `@errors`. Expected diagnostics SHALL carry the additional class `glosharp-error-expected`. Each diagnostic SHALL get an inline wavy underline (`text-decoration-style: wavy`; a wavy `border` is invalid CSS) on every affected line, and exactly one message box rendered as block content after the last affected line — never inside the underline or the code line, so the code line is never split. Zero-width diagnostics SHALL underline an adjacent character. Annotations SHALL carry the diagnostic severity and apply severity-specific styling: error (red), warning (yellow/amber), info (blue).

#### Scenario: Error annotation created
- **WHEN** the glosharp result contains an error at line 3
- **THEN** line 3 gets an inline underline, and a message box is rendered after line 3

#### Scenario: Code line is not split
- **WHEN** an error covers `missingVar` in `Console.WriteLine(missingVar);`
- **THEN** the rendered `.ec-line` contains the whole statement and the message box is outside it

#### Scenario: Expected errors are shown
- **WHEN** a block contains `// @errors: CS0029` and the CS0029 diagnostic occurs
- **THEN** the underline and message are rendered with the `glosharp-error-expected` class

#### Scenario: Warning annotation uses amber styling
- **WHEN** the glosharp result contains a warning diagnostic at line 5
- **THEN** the underline annotation uses amber/yellow decoration color and the message annotation uses amber styling

#### Scenario: Multi-line error annotation
- **WHEN** the glosharp result contains a diagnostic spanning lines 2-4
- **THEN** underline annotations are created for lines 2, 3, and 4, and the error message is placed after line 4

### Requirement: Popup content in hover annotations
Each hover annotation SHALL render the token wrapped in `span.glosharp-hover` together with a hidden `div.glosharp-popup-container` holding the popup content. Popup content SHALL include structured doc sections when available: summary text, a parameter list, return description, remarks, examples, and exception list. Each section SHALL be rendered in a distinct styled container.

#### Scenario: Popup with summary only
- **WHEN** a hover has `docs` with only `summary` populated
- **THEN** the popup renders the summary in a `.glosharp-popup-docs` div

#### Scenario: Popup with params and returns
- **WHEN** a hover has `docs` with `summary`, `params`, and `returns`
- **THEN** the popup renders the summary, followed by a params section listing each parameter name and description, followed by a returns section

#### Scenario: Popup with all doc sections
- **WHEN** a hover has `docs` with `summary`, `params`, `returns`, `remarks`, `examples`, and `exceptions`
- **THEN** the popup renders all sections in order: summary, params, returns, remarks, examples, exceptions

#### Scenario: Popup without docs
- **WHEN** a hover has `docs` as null
- **THEN** the popup renders only the type signature code, with no docs section

### Requirement: Well-formed HAST output
All annotation render functions SHALL build their output with hastscript (`h()` / `s()`) so the rendered tree contains no `root` node below the top level — including when overlapping annotations make EC pass `root` fragments as `nodesToTransform` — and all class names are set through `className`. Every render function SHALL return exactly as many nodes as it receives.

#### Scenario: Error overlapping hovered tokens
- **WHEN** `int y = DateTime.Now;` is rendered with hovers on `DateTime` and `Now` and an error spanning `DateTime.Now`
- **THEN** the rendered tree contains no nested `root` node and builds under Astro 7.3's Sätteri markdown processor

### Requirement: JavaScript-driven popup visibility
The plugin SHALL inject a JavaScript module via `jsModules` to manage hover popup visibility. The module SHALL register document-level delegated listeners exactly once per page (re-executing the module or client-side navigation SHALL NOT add listeners), so tokens added later need no re-binding. It SHALL show popups on pointer enter and on keyboard focus, hide them shortly after the pointer leaves the token and popup, on blur, on a tap outside, and on Escape; reparent the shown popup to the EC root for correct absolute positioning; give it `role="tooltip"` and link it from the token with `aria-describedby` while open; and re-trigger the fade-in animation. Arrow keys, Home and End SHALL move focus between the tokens of a block (roving tabindex). Popups SHALL open below the token and flip above it when there is not enough room below. The sprite sheet SHALL be re-added after an Astro view transition (`astro:after-swap`, `astro:page-load`).

#### Scenario: Popup visibility on hover
- **WHEN** a user hovers over a token with hover data
- **THEN** the JS module shows the popup container and positions it relative to the EC root

#### Scenario: Popup hidden on mouse leave
- **WHEN** the mouse leaves the hover token
- **THEN** the JS module hides the popup container

#### Scenario: Keyboard access
- **WHEN** a keyboard user tabs onto a hover token
- **THEN** its popup is shown; ArrowRight moves focus (and the popup) to the next token; Escape hides the popup and keeps focus on the token

#### Scenario: Astro view transition support
- **WHEN** Astro performs a view transition (page swap) any number of times
- **THEN** popups work on the new DOM and the number of document-level listeners does not grow

#### Scenario: Popup near the viewport bottom
- **WHEN** a popup would overflow the bottom of the viewport and there is more room above the token
- **THEN** the popup opens above the token

### Requirement: SVG sprite sheet for symbol icons
The plugin SHALL build an SVG sprite sheet containing `<symbol>` elements for each symbol kind icon (Method, Property, Field, Local, Class, Struct, Interface, Enum, Namespace, Event, Delegate, Type, Constant, EnumMember, Keyword, Operator). The sprite sheet SHALL be injected into the page DOM once via the JS module. Individual icon references SHALL use `<svg><use href="#glosharp-icon-{kind}"></svg>` elements to avoid repeating SVG path data.

#### Scenario: Sprite sheet injected once
- **WHEN** multiple code blocks with hover icons are rendered on a page
- **THEN** the sprite sheet `<svg>` with `<symbol>` definitions is injected into `document.body` exactly once (guarded by `#glosharp-sprites` ID check)

#### Scenario: Symbol icon uses sprite reference
- **WHEN** a hover popup displays a symbol kind icon (e.g., Method)
- **THEN** the icon renders as `<use href="#glosharp-icon-Method">` referencing the sprite sheet

### Requirement: Theme-aware styling via EC style settings
The plugin SHALL register a `PluginStyleSettings` instance under the `glosharp` key for popup colors (background, foreground, muted foreground, border), token hover/focus, error, warning and info colors and backgrounds, highlight, focus and diff colors, custom tag colors, and the syntax colors used inside popups. Values SHALL be resolved per EC theme: popup colors from the theme's `editorHoverWidget.*`/`editorWidget.*` colors (falling back to the theme background/foreground), syntax colors from the theme's token colors, status colors from `editorError/Warning/Info.foreground`, with text colors adjusted to at least 4.5:1 contrast on their background. `baseStyles` SHALL reference these settings only through EC CSS variables (`cssVar`), so theme switching follows EC's `useDarkModeMediaQuery` / `themeCssSelector`; the plugin SHALL NOT rely on `[data-theme]` selectors. Users SHALL be able to override every value via `styleOverrides.glosharp`.

#### Scenario: Dark theme popup styling
- **WHEN** the EC instance uses a dark theme
- **THEN** popups use that theme's dark hover-widget colors

#### Scenario: Light theme popup styling
- **WHEN** the EC instance uses `github-light`
- **THEN** popups, completion lists and static `^?` results have a light background with readable text, in the same page that renders `github-dark` blocks dark

#### Scenario: Plugin works with EC 0.41 without workaround
- **WHEN** `pluginGloSharp()` is added directly to an expressive-code `plugins` array in EC 0.41+
- **THEN** the plugin registers without errors and no consumer-side property stripping is needed

### Requirement: Hover token interaction styles
Hoverable tokens SHALL have a transparent dashed bottom border by default. When the EC container (`.expressive-code`) is hovered, all hoverable tokens within SHALL show a subtle dashed underline via `color-mix(in srgb, currentColor 40%, transparent)`. When a specific token is directly hovered, keyboard-focused or has its popup open, it SHALL display a solid underline, a subtle purple background (the `tokenHoverBackground` style setting), and `border-radius: 2px`; a keyboard-focused token SHALL additionally show a visible focus outline. The border-radius SHALL only apply in those states, not in the resting state.

#### Scenario: Token resting state
- **WHEN** a code block is rendered with auto-hover data
- **THEN** hoverable tokens appear as normal code with no visible decoration

#### Scenario: Container hover reveals underlines
- **WHEN** the user hovers anywhere over the code block
- **THEN** all hoverable tokens show a subtle dashed underline

#### Scenario: Direct token hover highlights
- **WHEN** the user hovers directly over a specific token
- **THEN** that token shows a solid underline, purple background tint, and rounded corners

### Requirement: Popup fade-in animation
Popups SHALL use a CSS `@keyframes` animation (`glosharpPopupFadeIn`) that fades in from `opacity: 0` with a slight upward translation (`translateY(-4px)`). The animation SHALL be re-triggered by the JS module each time a popup is shown. A `prefers-reduced-motion: reduce` media query SHALL disable transitions on hover elements.

#### Scenario: Popup animation on show
- **WHEN** a hover popup becomes visible
- **THEN** it fades in with a subtle upward slide over 0.12s

#### Scenario: Reduced motion preference
- **WHEN** the user has `prefers-reduced-motion: reduce` enabled
- **THEN** hover transitions are disabled

### Requirement: Process all C# code blocks
By default the plugin SHALL invoke glosharp processing on ALL C# code blocks (`csharp`, `cs`, `c#`), regardless of whether they contain glosharp markers. Non-C# code blocks SHALL be skipped. A block whose meta contains `no-glosharp` or `glosharp=false` SHALL be skipped without invoking the CLI. With the `explicitTrigger: true` option, only C# blocks whose meta contains `glosharp` SHALL be processed.

#### Scenario: C# block without markers is processed
- **WHEN** a C# code block contains `var x = 42;` with no glosharp markers
- **THEN** the plugin invokes glosharp processing and produces auto-hover annotations

#### Scenario: Non-C# block still skipped
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke glosharp processing

#### Scenario: Opt-out
- **WHEN** a C# block's meta contains `no-glosharp`
- **THEN** the CLI is not invoked and the block renders as plain highlighted code

#### Scenario: Explicit trigger
- **WHEN** `explicitTrigger: true` is set
- **THEN** a C# block is processed only if its meta contains `glosharp`

### Requirement: Error policy
The plugin SHALL NOT silently swallow failures.
- When the CLI cannot process a block (not found, failed to spawn, non-zero exit, invalid output), the plugin SHALL by default (`onCliError: 'throw'`) fail with an error naming the document path, the block position and its first line, the CLI error, install instructions when the CLI could not be started, and how to downgrade to a warning. With `onCliError: 'warn'` it SHALL log the full error once (subsequent identical failures as one-line notices) and render the block unprocessed.
- Unexpected compile errors — error-severity diagnostics not marked `expected`, `hiddenErrors` (errors in hidden code), and unmatched `@errors` expectations reported by the CLI — SHALL be logged through EC's logger as one warning per block naming the document, block and 1-based source line (using `sourceLine` when provided). With `failOnErrors: true` the plugin SHALL throw instead. Warning- and info-severity diagnostics and expected errors SHALL NOT be reported.
- Each entry of `meta.warnings` SHALL be logged as a warning with the block location.

#### Scenario: CLI missing
- **WHEN** the CLI cannot be found and `onCliError` is not set
- **THEN** the build fails with the block location and `dotnet tool install` instructions

#### Scenario: CLI missing, warn mode
- **WHEN** the CLI cannot be found and `onCliError: 'warn'` is set
- **THEN** one warning with install instructions is logged and blocks render without type information

#### Scenario: Unexpected error
- **WHEN** a block produces an unexpected CS0029 on source line 2
- **THEN** a warning containing the document path, block position and `line 2: CS0029` is logged and the error is rendered

#### Scenario: failOnErrors
- **WHEN** `failOnErrors: true` is set and a block produces an unexpected error
- **THEN** the build fails with the same details

### Requirement: Render default hovers as mouse-over popups
For hovers with `persistent: false`, the plugin SHALL render a `<span class="glosharp-hover">` wrapper around the token. The popup SHALL only be visible on hover interaction (controlled by JS). The token SHALL NOT have any visible underline or decoration in its default state — it should appear as normal code until hovered.

#### Scenario: Default hover invisible until interaction
- **WHEN** a code block is rendered with auto-hover data for token `x`
- **THEN** the token `x` appears as normal code with no visual decoration, and hovering reveals the type popup

#### Scenario: Default hover popup content
- **WHEN** a user hovers over a token with auto-hover data
- **THEN** the popup displays the same structured content (type signature, display parts, docs)

### Requirement: Block content after lines
Persistent `^?` results, completion lists, diagnostic messages and custom tag callouts for a line SHALL be collected into a single line-level annotation rendered in the `latest` phase as `<div class="glosharp-line">` containing the untouched `.ec-line` followed by `<div class="glosharp-line-extras">` (in order: static results, completion lists, messages, callouts). The block thus keeps exactly one top-level node per line. Block content SHALL be limited to the visible width of the code block and stay in view when the code scrolls horizontally.

#### Scenario: Several items on one line
- **WHEN** one line has a `^?` result, a warning and an `@log` callout
- **THEN** a single `.glosharp-line` wrapper holds the `.ec-line` and one `.glosharp-line-extras` with the static result, the message and the callout in that order

### Requirement: Render persistent hovers as always-visible static annotations
For hovers with `persistent: true` (from `^?` markers), the plugin SHALL render a `<div class="glosharp-static">` containing a `<div class="glosharp-static-container">` in the line's block content, aligned under the queried column where space allows. The popup SHALL be always visible without requiring interaction and SHALL NOT be a focus target. No arrow caret SHALL be displayed on static containers. Static containers SHALL participate in normal document flow, reserving vertical space, so they never overlap subsequent code lines or other static containers.

#### Scenario: Persistent hover always visible
- **WHEN** a code block contains a `^?` marker targeting token `x`
- **THEN** the hover popup for `x` is rendered in an always-visible state below the code line

#### Scenario: Static popups do not overlap content
- **WHEN** a code block contains multiple `^?` markers on consecutive lines
- **THEN** every code line and every static container remains fully visible — no static container's box intersects another code line's or static container's box

### Requirement: Pass-through for non-glosharp code blocks
The plugin SHALL not modify code blocks that are not C# language blocks, or C# blocks that opted out (see "Process all C# code blocks"). Other C# code blocks SHALL be processed for auto-hover extraction regardless of marker presence.

#### Scenario: Non-C# code block
- **WHEN** a JavaScript code block enters the EC pipeline
- **THEN** the plugin does not invoke the CLI or add any annotations

#### Scenario: C# without markers still processed
- **WHEN** a C# code block without `^?`, `@errors`, or other glosharp markers enters the pipeline
- **THEN** the plugin invokes glosharp processing and adds auto-hover annotations for all semantically meaningful tokens

### Requirement: Pass project and region options to bridge
The `pluginGloSharp()` factory SHALL accept `project` and `region` options and pass them through to the glosharp bridge when processing code blocks. A block's `region="name"` meta option SHALL override the `region` option for that block.

#### Scenario: Plugin with project context
- **WHEN** `pluginGloSharp({ project: './MyProject.csproj' })` is configured
- **THEN** all glosharp CLI invocations include the `--project` argument

#### Scenario: Plugin without project
- **WHEN** `pluginGloSharp()` is configured without a `project` option
- **THEN** CLI invocations use standalone mode (framework refs only)

#### Scenario: Plugin with region
- **WHEN** `pluginGloSharp({ region: 'example' })` is configured
- **THEN** all glosharp CLI invocations include the `--region` argument (together with `--stdin`)

#### Scenario: Per-block region
- **WHEN** a block's meta contains `region="setup"`
- **THEN** that block's CLI invocation includes `--region setup`

### Requirement: Add completion annotations in annotateCode hook
The `annotateCode` hook SHALL render a completion list for each completion result as block content after the queried line (see "Block content after lines"), showing item kinds, labels and details in aligned columns, using popup colors.

#### Scenario: Completion annotation created
- **WHEN** the glosharp result contains completions at line 2, character 8
- **THEN** a completion list with the items is rendered after line 2

#### Scenario: Render is EC-core valid
- **WHEN** a block containing a `^|` completion marker is rendered through the Expressive Code engine
- **THEN** rendering completes without EC core rejecting the annotation output, and the emitted HTML contains the `.glosharp-completion-list`

### Requirement: Detect completion markers for processing
The marker detection logic SHALL recognize `^|` markers in addition to `^?` markers when deciding whether to invoke glosharp processing on a code block.

#### Scenario: Block with only completion markers
- **WHEN** a C# code block contains `^|` markers but no `^?` markers
- **THEN** the plugin invokes glosharp processing on the block

### Requirement: Theme-aware styling for doc sections
The plugin SHALL define CSS classes for each doc section (`.glosharp-popup-params`, `.glosharp-popup-returns`, `.glosharp-popup-remarks`, `.glosharp-popup-example`, `.glosharp-popup-exceptions`) with styles consistent with the existing popup design. Parameter names SHALL be visually distinct (e.g., monospace or bold).

#### Scenario: Param list styling
- **WHEN** a popup with params is rendered
- **THEN** each parameter is displayed with its name in a distinct style (code font) followed by its description

#### Scenario: Section separators
- **WHEN** a popup has multiple doc sections
- **THEN** each section is visually separated (consistent with the existing `.glosharp-popup-docs` border-top pattern)

### Requirement: Detect directive markers for processing
The marker detection logic SHALL recognize `@highlight`, `@focus`, and `@diff` markers in addition to existing markers when deciding whether to invoke glosharp processing on a code block.

#### Scenario: Block with only highlight markers
- **WHEN** a C# code block contains `// @highlight` but no `^?` or `@errors` markers
- **THEN** the plugin invokes glosharp processing on the block

#### Scenario: Block with only diff markers
- **WHEN** a C# code block contains `// @diff: +` but no other glosharp markers
- **THEN** the plugin invokes glosharp processing on the block

### Requirement: Add highlight annotations in annotateCode hook
The `annotateCode` hook SHALL add the class `glosharp-highlight` to the rendered `.ec-line` of each highlight entry with `kind: "highlight"` (no wrapper element), applying a background color to the entire line.

#### Scenario: Highlight annotation created
- **WHEN** the glosharp result contains a highlight with `kind: "highlight"` at line 2
- **THEN** the `.ec-line` for line 2 has the `glosharp-highlight` class

#### Scenario: Highlight annotation rendering
- **WHEN** the annotation renders in the EC pipeline
- **THEN** the line has a visible background color distinguishing it from non-highlighted lines

### Requirement: Add focus annotations in annotateCode hook
When any focus entries exist, the `annotateCode` hook SHALL add the class `glosharp-focus-dim` to the `.ec-line` of every line without a `kind: "focus"` entry, dimming it. Focused lines SHALL remain at full opacity.

#### Scenario: Focus annotation dims non-focused lines
- **WHEN** the glosharp result contains focus entries for lines 2 and 3 in a 5-line block
- **THEN** lines 0, 1, and 4 are rendered with reduced opacity, while lines 2 and 3 remain at full opacity

#### Scenario: No focus entries means no dimming
- **WHEN** the glosharp result contains no focus entries
- **THEN** all lines render at full opacity (no dimming applied)

### Requirement: Add diff annotations in annotateCode hook
The `annotateCode` hook SHALL add the class `glosharp-diff-add` or `glosharp-diff-remove` to the `.ec-line` of each diff entry. Lines with `kind: "add"` SHALL have a green-tinted background and line border; lines with `kind: "remove"` a red-tinted background and line border.

#### Scenario: Diff add annotation rendering
- **WHEN** the glosharp result contains a highlight with `kind: "add"` at line 3
- **THEN** line 3 is rendered with a green-tinted background color

#### Scenario: Diff remove annotation rendering
- **WHEN** the glosharp result contains a highlight with `kind: "remove"` at line 4
- **THEN** line 4 is rendered with a red-tinted background color

### Requirement: Theme-aware styling for highlight, focus, and diff
Highlight background, focus dimmed opacity, and diff colors SHALL be glosharp style settings with per-theme (dark/light) values.

#### Scenario: Highlight in dark theme
- **WHEN** the EC instance uses a dark theme
- **THEN** highlighted lines use a dark-appropriate background color

### Requirement: Render clickable error codes in error messages
Error messages SHALL render error codes matching `CS\d+` as `<a>` elements linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{code}`. Links SHALL open in a new tab with `rel="noopener"`. Non-CS codes SHALL remain plain text.

#### Scenario: CS error code linked
- **WHEN** an error message with code `CS1002` is rendered in the EC pipeline
- **THEN** the error code is an `<a>` element with href to the Microsoft docs page and `target="_blank"`

#### Scenario: Analyzer code not linked
- **WHEN** an error message with code `CA1234` is rendered
- **THEN** the error code is plain text without a link

### Requirement: Add custom tag annotations in annotateCode hook
The `annotateCode` hook SHALL render a callout box for each tag in the glosharp result as block content after the associated code line.

#### Scenario: Tag annotation created
- **WHEN** the glosharp result contains a tag with `name: "log"` at line 2
- **THEN** a callout with the tag name and message is rendered after line 2

#### Scenario: Multiple tag annotations
- **WHEN** the glosharp result contains tags on different lines
- **THEN** each line receives its own callout with the correct name and message

### Requirement: Custom tag callout rendering
The `GloSharpCustomTagAnnotation` SHALL render as a block-level callout box below the code line. The box SHALL contain: an SVG icon specific to the tag type, the tag name as a title, and the message text as content. The rendered structure SHALL use the CSS classes `glosharp-tag` (base) and `glosharp-tag-{name}` (tag-specific).

#### Scenario: Log callout rendered
- **WHEN** a `log` tag annotation renders
- **THEN** the output contains a callout box with an info icon, "log" title, and the message text, with CSS class `glosharp-tag-log`

#### Scenario: Warn callout rendered
- **WHEN** a `warn` tag annotation renders
- **THEN** the output contains a callout box with a warning icon, "warn" title, and the message text, with CSS class `glosharp-tag-warn`

#### Scenario: Error callout rendered
- **WHEN** an `error` tag annotation renders
- **THEN** the output contains a callout box with an error icon, "error" title, and the message text, with CSS class `glosharp-tag-error`

#### Scenario: Annotate callout rendered
- **WHEN** an `annotate` tag annotation renders
- **THEN** the output contains a callout box with a lightbulb icon, "annotate" title, and the message text, with CSS class `glosharp-tag-annotate`

### Requirement: Theme-aware styling for custom tag callouts
The plugin SHALL define theme-aware CSS for custom tag callouts with tag-specific colors: log (blue), warn (amber), error (red), annotate (purple). Each tag type SHALL have distinct background, border, and icon colors, defined as glosharp style settings resolved per EC theme.

#### Scenario: Dark theme tag styling
- **WHEN** the EC instance uses a dark theme
- **THEN** tag callout boxes use dark-appropriate background and border colors

#### Scenario: Light theme tag styling
- **WHEN** the EC instance uses a light theme
- **THEN** tag callout boxes use light-appropriate background and border colors via the theme's glosharp style settings

### Requirement: Detect custom tag markers for processing
The marker detection logic SHALL recognize `@log:`, `@warn:`, `@error:`, and `@annotate:` markers in addition to existing markers when deciding whether to invoke glosharp processing on a code block.

#### Scenario: Block with only tag markers
- **WHEN** a C# code block contains `// @log: message` but no `^?` or `@errors` markers
- **THEN** the plugin invokes glosharp processing on the block

### Requirement: Popup viewport clamping
Hover popups SHALL remain fully within the visual viewport horizontally: popup styles SHALL cap the popup width to the viewport (viewport-aware max-width with content wrapping when constrained), and the positioning logic SHALL clamp the popup's horizontal position so neither edge extends past the viewport, while keeping the popup vertically adjacent to its token.

#### Scenario: Wide popup on a narrow viewport
- **WHEN** a popup whose natural width exceeds 390px opens in a 390px-wide viewport
- **THEN** the popup's bounding box lies fully within the viewport and remains vertically adjacent to its token

#### Scenario: Token near the right edge
- **WHEN** a popup opens for a token near the right edge of the viewport
- **THEN** the popup shifts left as needed so its right edge stays within the viewport
