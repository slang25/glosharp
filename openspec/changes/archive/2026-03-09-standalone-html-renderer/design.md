## Context

GloSharp currently outputs JSON metadata via `glosharp process`, consumed by JavaScript integrations (Shiki transformer, EC plugin). Users without a Node.js pipeline (Hugo, Jekyll, plain HTML, quick previews) have no way to get rendered output. The architecture diagram already includes a "Standalone renderer" path producing "Static HTML" for "Anywhere."

The core already has everything needed for metadata extraction — `GloSharpProcessor.ProcessAsync()` returns a `GloSharpResult` with hovers, errors, highlights, and completions. What's missing is syntax highlighting (token classification) and HTML generation.

## Goals / Non-Goals

**Goals:**
- Add `glosharp render` CLI command producing self-contained HTML fragments
- Use Roslyn's Classifier for C# syntax highlighting (no TextMate/Shiki dependency)
- Ship built-in themes (github-dark, github-light) with a clear extension point
- CSS-only interactivity (anchor positioning for popups, no JavaScript in output)
- Reuse CSS patterns from the EC plugin for visual consistency

**Non-Goals:**
- TextMate grammar bundling or Shiki integration in the .NET tool
- Custom theme file format or VS Code theme import
- Runtime JavaScript in rendered output
- Changes to the existing JSON output, Node bridge, or EC plugin
- Syntax highlighting for languages other than C#

## Decisions

### 1. Syntax highlighting via Roslyn Classifier

**Decision**: Use `Classifier.GetClassifiedSpansAsync()` from `Microsoft.CodeAnalysis.CSharp.Workspaces`.

**Alternatives considered**:
- **TextMate grammars**: Would require bundling grammar files and a TextMate engine. Adds complexity for no gain since we already have a Roslyn compilation.
- **Roslyn's SyntaxTokenClassifier (manual)**: Walking the syntax tree manually. More work, less accurate than the built-in Classifier which handles semantic classification (e.g., distinguishing class names from local variables).

**Rationale**: The Classifier API is already available via `Microsoft.CodeAnalysis.CSharp.Workspaces` (implied by our `Microsoft.CodeAnalysis.CSharp.Features` dependency). It classifies tokens semantically — it knows `Console` is a class name, not just an identifier. This gives us VS-quality highlighting with zero additional dependencies. We need a `Document` in a `Workspace` to use it, but we can create an `AdhocWorkspace` (similar to how completions already work in the processor).

### 2. Theme system: built-in theme records

**Decision**: Define themes as C# records mapping Roslyn classification names to hex colors. Ship `github-dark` and `github-light`. Theme selection via `--theme` CLI flag (default: `github-dark`).

**Alternatives considered**:
- **VS Code theme JSON import**: Powerful but complex — VS Code themes use TextMate scopes, not Roslyn classifications. Mapping would be approximate and add parsing complexity.
- **CSS custom properties only**: Let users provide their own CSS. Too much friction for the "just works" use case.

**Rationale**: A simple mapping (`keyword → #ff7b72`, `className → #f0883e`, etc.) covers the common case. The theme record is a plain data structure, easy to extend later. Two built-in themes cover light/dark. Users who need custom themes can post-process the CSS or contribute new theme records.

### 3. HTML output: fragment by default, standalone optional

**Decision**: Default output is an embeddable `<div>` fragment with inline `<style>`. `--standalone` wraps in a full HTML page.

**Rationale**: Most users embedding in Hugo/Jekyll want a fragment. The `<style>` is inlined so fragments are self-contained — no external CSS file needed. A `--standalone` flag adds `<!DOCTYPE html><html><head>` wrapping for quick previews or direct browser viewing.

### 4. Popup positioning: CSS anchor positioning with fallback

**Decision**: Use CSS `anchor-name` / `position-anchor` for hover popups. Include `@supports not (anchor-name: --x)` fallback using absolute positioning.

**Rationale**: Per decision 005 in the design docs, all major browsers now support CSS anchor positioning. The fallback ensures older browsers still show popups (just with less precise positioning). No JavaScript needed.

### 5. Architecture: SyntaxClassifier + HtmlRenderer in GloSharp.Core

**Decision**: Add two new classes to `GloSharp.Core`:
- `SyntaxClassifier` — wraps Roslyn's `Classifier.GetClassifiedSpansAsync()`, returns classified spans for a source/compilation pair
- `HtmlRenderer` — takes a `GloSharpResult` + classified spans + theme, produces HTML string

The CLI `render` command orchestrates: process → classify → render → output.

**Alternatives considered**:
- **Separate project (GloSharp.Renderer)**: Adds project complexity for a small amount of code. The renderer depends heavily on Core types already.
- **All in CLI**: Would prevent reuse by anyone who wants to call the renderer programmatically.

**Rationale**: Keeping it in Core allows the Node bridge to expose `render` functionality later if needed. The `HtmlRenderer` is a pure function (data in, string out) with no side effects.

### 6. Popup content rendering: reuse hover `parts` with theme colors

**Decision**: Render hover popup content using the `parts` array from `GloSharpHover`, applying the same theme colors to part kinds. This gives syntax-highlighted type signatures in popups.

**Rationale**: The EC plugin already does this (re-renders type info with syntax highlighting). Since we have the `parts` array with semantic kinds (`keyword`, `className`, `localName`, etc.), we can map them to the same theme colors used for code tokens. The `docs` field (when present) is rendered as plain text below the type signature.

## Risks / Trade-offs

**[Classifier requires a Workspace]** → We already create an `AdhocWorkspace` for completions. The classifier will reuse this pattern. Minor overhead of creating a `Document` from the already-compiled syntax tree and compilation.

**[Theme colors may not perfectly match Shiki/EC output]** → Roslyn's classification kinds don't map 1:1 to TextMate scopes. Users comparing standalone HTML to EC-rendered output may see slight color differences. Acceptable trade-off — the standalone renderer is for environments without Shiki/EC.

**[Inline `<style>` may conflict with page CSS]** → CSS class names use `glosharp-` prefix to minimize conflicts. The inline style block is scoped to `.glosharp-code` container. If multiple glosharp blocks appear on one page, the style block is duplicated — a minor size concern.

**[CSS anchor positioning fallback is less precise]** → The absolute positioning fallback positions popups relative to the hover span's parent, not the viewport. Popups may overlap or clip in some layouts. This matches behavior users would expect from older browser support.
