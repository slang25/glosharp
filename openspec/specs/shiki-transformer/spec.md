# shiki-transformer Specification

## Purpose
The `@glosharp/shiki` transformer that injects hovers, errors and completions into Shiki output.

## Requirements
### Requirement: Markdown integration selects Glo# blocks
`@glosharp/shiki` SHALL export `remarkGloSharp(options)` (a unified/remark plugin) and `satteriGloSharp(options)` (a Sätteri mdast plugin, Astro 7.3+). A fenced block is a Glo# block when its language is one of `languages` (default `csharp`, `cs`, `c#`) or is `glosharp`, its meta does not contain `no-glosharp`, and either its meta contains `glosharp`, the block contains Glo# markers (`hasGloSharpMarkers`), or `processUnmarked` is set. With `explicitTrigger: true` only blocks whose meta contains `glosharp` (or `glosharp` fences) are processed; a RegExp `explicitTrigger` is tested against the meta. `region=` and `framework=` in the meta override those options for the block.

#### Scenario: Code block with markers
- **WHEN** a `csharp` code block contains `^?` markers
- **THEN** it is compiled and rendered with hovers

#### Scenario: Code block without markers
- **WHEN** a `csharp` code block has no glosharp markers and no `glosharp` meta
- **THEN** it is left unchanged

#### Scenario: Opt-out
- **WHEN** a block's meta contains `no-glosharp`
- **THEN** it is left unchanged, even with markers

### Requirement: Markdown integration renders or hands off results
All selected blocks in a document SHALL be processed concurrently through one bridge instance per configuration. With `render: 'html'` the plugin SHALL highlight the block with Shiki (default dual themes github-light/github-dark) and replace it — with a JSX `<div className="glosharp-block" dangerouslySetInnerHTML>` element when the processor compiles to JavaScript (MDX, Docusaurus), otherwise with HAST via `data.hChildren`. With `render: 'transformer'` it SHALL leave the block for the site's Shiki pass, append `glosharp-key=<snippetKey>` to the fence meta, and register the result for `transformerGloSharp()`, which looks results up by that key (falling back to the code). `render: 'auto'` (default) SHALL choose `'transformer'` when `transformerGloSharp()` has been created in the process. `satteriGloSharp` always hands off.

#### Scenario: Docusaurus
- **WHEN** `remarkGloSharp` runs in an MDX compile
- **THEN** the block becomes a `div.glosharp-block` JSX element holding the Shiki HTML

#### Scenario: Astro
- **WHEN** `remarkGloSharp` (or `satteriGloSharp`) runs and `transformerGloSharp()` is in `shikiConfig.transformers`
- **THEN** Astro's Shiki pass renders the processed code with hovers

### Requirement: Markdown integration error policy
Unexpected compile errors (`meta.compileSucceeded` false) SHALL be logged by default with the file and line of the fence and of each error (`file:line:col CODE: message`, using `sourceLine`/`sourceCharacter`); `onCompileError: 'throw'` SHALL fail the build and `'ignore'` SHALL stay quiet. `meta.warnings` SHALL be logged. A missing or failing CLI SHALL fail the build by default with the file and line and the CLI's message (which includes install instructions when it is not found); `onCliError: 'warn'` SHALL log each distinct failure once and render the block as plain code.

#### Scenario: Unexpected error
- **WHEN** a block at `docs/page.md:3` has CS0103 on its second line
- **THEN** a warning names `docs/page.md:3` and `docs/page.md:5:<col> CS0103`

#### Scenario: CLI missing
- **WHEN** the CLI cannot be found
- **THEN** the build fails with a message saying how to install it, unless `onCliError: 'warn'`

### Requirement: Lines are matched by index
Annotations SHALL be placed on Shiki's line elements by index (`this.lines`), so other transformers that add classes to lines (`@shikijs/transformers` notation/meta highlight, `addClassToHast`) do not shift or drop them.

#### Scenario: Highlighted line
- **WHEN** another transformer turns line 1's class into `line highlighted`
- **THEN** hovers and errors still land on lines 1, 2 and 3 as in the result

### Requirement: Inject hover popups in root hook
The `root` hook SHALL wrap each hover target in `<span class="glosharp-hover">` with `anchor-name: --<scope>-<n>`, where `<scope>` is derived from the result (deterministic across builds and processes). The popup `<span class="glosharp-popup" role="tooltip" id="<scope>-<n>">` SHALL be a child of the wrapper (not a sibling), so `:hover` on the wrapper keeps it open while the pointer is over the popup and the popup can be positioned relative to the wrapper when anchor positioning is unavailable. Wrappers SHALL be focusable (`tabindex="0"`, `aria-describedby` the popup) unless `focusable: false`. Hover ranges that cover part of a Shiki token SHALL split the token, keeping its colour, and keep the persistent class. Popups SHALL show the signature, `(+ N overloads)`, type annotations, and the summary, params, returns, exceptions and remarks from the docs.

#### Scenario: Token with hover data
- **WHEN** a token at line 0, character 4 has associated hover data
- **THEN** the HAST tree contains a `<span class="glosharp-hover">` with `anchor-name: --<scope>-0` wrapping the token, containing a child `<span class="glosharp-popup">` with `position-anchor: --<scope>-0` and the formatted hover text

#### Scenario: Pointer moves onto the popup
- **WHEN** the pointer moves from the hovered token onto its open popup
- **THEN** the popup remains open, because the popup is inside the wrapper's `:hover` subtree

#### Scenario: Keyboard
- **WHEN** a hover target receives keyboard focus
- **THEN** its popup is shown

#### Scenario: Rebuild
- **WHEN** the same result is rendered twice, in the same or different processes
- **THEN** the HTML is identical

### Requirement: Persistent queries are always visible
Hovers with `persistent: true` (`^?`) SHALL be rendered as a `<span class="glosharp-query">` block on its own row after the line, indented to the token's column, containing the popup content; the token gets `glosharp-hover glosharp-hover-persistent`.

#### Scenario: `^?` query
- **WHEN** the result has a persistent hover on line 0
- **THEN** the query box is visible below line 0 without hovering

### Requirement: Inject error annotations in root hook
The `root` hook SHALL wrap each diagnostic's range in `<span class="glosharp-error-underline glosharp-severity-<severity>">` (each affected line for multi-line spans; zero-length diagnostics underline one character) and put a `<span class="glosharp-error-message glosharp-severity-<severity>">` on its own row after the (last) affected line. Error codes matching `CS\d+` SHALL be rendered as `<a>` elements linking to Microsoft docs. Expected diagnostics SHALL be rendered too, with the extra class `glosharp-error-expected`.

#### Scenario: Error at a position
- **WHEN** the glosharp result contains an error at line 3, character 8
- **THEN** the HAST tree contains an error underline span at that position and an error message element with the diagnostic text

#### Scenario: Warning with severity class
- **WHEN** the glosharp result contains a warning diagnostic
- **THEN** the underline and message elements have class `glosharp-severity-warning`

#### Scenario: Error code linked to docs
- **WHEN** the glosharp result contains a diagnostic with code `CS0246`
- **THEN** the error code in the message element is an `<a>` linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246`

#### Scenario: Multi-line diagnostic underlines
- **WHEN** the glosharp result contains a diagnostic spanning lines 2-4
- **THEN** underline elements are applied to lines 2, 3, and 4, and the error message appears after line 4

#### Scenario: No blank row
- **WHEN** an error message follows a line
- **THEN** it is inserted after the line's newline, so no empty row appears below it

### Requirement: Highlights, focus and diff
Lines with `highlight`, `add` and `remove` entries SHALL get `glosharp-highlight`, `glosharp-diff-add` and `glosharp-diff-remove`; when any line has `focus`, focused lines get `glosharp-focused`, the others `glosharp-focus-dim`, and the `<pre>` gets `glosharp-has-focus`.

#### Scenario: Focus
- **WHEN** the result focuses line 3 of 4
- **THEN** lines 0–2 are dimmed until the block is hovered or focused

### Requirement: Custom tags
Each `@log`/`@warn`/`@error`/`@annotate` tag SHALL be rendered as `<span class="glosharp-tag glosharp-tag-<name>">` on its own row after its line.

#### Scenario: Log tag
- **WHEN** the result has `{ name: 'log', text: 'cached', line: 0 }`
- **THEN** a log row with "cached" follows line 0

### Requirement: CSS anchor positioning for popups
Hover popups SHALL use CSS anchor positioning (`anchor-name`, `position-anchor`, `position-area: bottom span-right` with `position-try-fallbacks` flipping above when there is no room) and be shown via `:hover` and `:focus-visible`. No JavaScript SHALL be required. The `<pre>` SHALL set `anchor-scope: all` so identical blocks on a page do not share anchors. The stylesheet SHALL include an `@supports not (anchor-name: --a)` fallback that positions the popup absolutely below its hover wrapper, so popups remain usable — degraded positioning is acceptable, a popup detached from its token is not.

#### Scenario: Hover popup visibility
- **WHEN** the rendered HTML is viewed in a browser
- **THEN** hovering over a token with hover data shows a popup positioned next to the token using CSS anchoring, with no JS execution

#### Scenario: Browser without anchor positioning
- **WHEN** the rendered HTML is viewed in a browser where `anchor-name` is unsupported (or the anchor properties are stripped)
- **THEN** hovering the token still shows the popup adjacent to the hover wrapper, within a relaxed positional tolerance

### Requirement: Popups follow the theme
The `<pre>` SHALL get `glosharp`, `glosharp-theme-dark` or `glosharp-theme-light` (from the default theme's background), `glosharp-dual` for dual-theme output, and `--glosharp-bg` / `--glosharp-fg` from the theme. Popups, query boxes and completion lists SHALL use those colours; every element Glo# adds SHALL be a `<span>` that declares its own `--shiki-light` / `--shiki-dark` where it has its own colour, so Shiki's standard dark-mode rule recolours it correctly. The stylesheet SHALL switch dual-theme Glo# blocks to their dark colours under `html.dark` and `html[data-theme="dark"]`.

#### Scenario: Light page
- **WHEN** a block is rendered with `github-light`
- **THEN** its popups have a light background and light-palette part colours

#### Scenario: Docusaurus dark mode
- **WHEN** a dual-theme block is shown with `html[data-theme="dark"]`
- **THEN** the code, popups and messages use the dark theme colours

### Requirement: Render structured parts with syntax highlighting
Hover popup content SHALL render the display parts array with appropriate CSS classes for each part kind (e.g., `glosharp-keyword`, `glosharp-className`) to enable syntax-highlighted type information.

#### Scenario: Hover popup content styling
- **WHEN** hover parts include `{kind: "keyword", text: "int"}` and `{kind: "localName", text: "x"}`
- **THEN** the popup HTML contains `<span class="glosharp-part glosharp-keyword">int</span>` and `<span class="glosharp-part glosharp-localName">x</span>`

### Requirement: Pass options to the bridge
The batch and Markdown integrations SHALL accept `project`, `region`, `framework`, `noRestore` and the bridge options (`executable`, `cacheDir`, `configFile`, `complog`, `complogProject`, `concurrency`, `timeoutMs`) and pass them to the bridge.

#### Scenario: Project context
- **WHEN** `remarkGloSharp({ project: './MyProject.csproj' })` is configured
- **THEN** all glosharp CLI invocations include the `--project` argument

### Requirement: Render completion lists in HAST
The `root` hook SHALL inject a `<span class="glosharp-completions">` row after the line for each completion result with items, indented to the caret's column, containing a `<span class="glosharp-completion-list">` of `<span class="glosharp-completion-item glosharp-completion-kind-<Kind>">` items. Items SHALL be deduplicated, filtered to those starting with the identifier typed before the caret (all items when none match), and limited to `completionLimit` (default 12) with a "… N more" row.

#### Scenario: Completion list rendered
- **WHEN** the glosharp result contains completions at line 2, character 8
- **THEN** the HAST tree contains a `glosharp-completion-list` element after line 2

#### Scenario: Completion item display
- **WHEN** a completion item has `label: "WriteLine"`, `kind: "Method"`, `detail: "void Console.WriteLine(string?)"`
- **THEN** the rendered item includes the label text, a kind badge (`glosharp-completion-kind`) and class `glosharp-completion-kind-Method`, and the detail text

#### Scenario: Typed prefix
- **WHEN** the code is `list.Ad` with the caret after `Ad`
- **THEN** only items starting with `Ad` are listed, with the matched prefix in `glosharp-completion-match`

#### Scenario: No completions renders nothing
- **WHEN** the glosharp result has an empty completions array (or a completion with no items)
- **THEN** no completion list elements are injected into the HAST

### Requirement: Packaged stylesheet
The package SHALL publish its stylesheet as `@glosharp/shiki/style.css` (`dist/style.css`).

#### Scenario: Installed from the registry
- **WHEN** a consumer imports `@glosharp/shiki/style.css` from the packed tarball
- **THEN** the file resolves
