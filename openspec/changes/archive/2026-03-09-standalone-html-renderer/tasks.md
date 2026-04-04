## 1. Syntax Classification

- [x] 1.1 Add `SyntaxClassifier` class to `GloSharp.Core` that wraps `Classifier.GetClassifiedSpansAsync()` — creates an `AdhocWorkspace`/`Document` from the existing compilation and source, returns classified spans
- [x] 1.2 Add classification name mapping method that normalizes Roslyn's classification type strings (`"class name"` → `className`, `"string - verbatim"` → `string`, etc.) to theme-friendly keys
- [x] 1.3 Add `GloSharpTheme` record type with color mappings for all classification keys plus popup/error/highlight/diff colors, background, and foreground
- [x] 1.4 Implement `github-dark` built-in theme with color values matching GitHub's dark color scheme
- [x] 1.5 Implement `github-light` built-in theme with color values matching GitHub's light color scheme
- [x] 1.6 Add tests for `SyntaxClassifier` — verify keywords, strings, class names, and comments are classified correctly
- [x] 1.7 Add tests for classification name mapping — verify known Roslyn types map correctly and unknown types fall back to `text`

## 2. HTML Renderer

- [x] 2.1 Add `HtmlRenderer` class to `GloSharp.Core` with main `Render(GloSharpResult, ClassifiedSpan[], GloSharpTheme, HtmlRenderOptions)` method returning an HTML string
- [x] 2.2 Implement syntax-highlighted `<pre><code>` generation — iterate classified spans, emit `<span style="color:...">` for each token using theme colors
- [x] 2.3 Implement hover popup rendering — wrap hover target tokens in `<span class="glosharp-hover">` with unique `anchor-name`, emit sibling `<div class="glosharp-popup">` with parts rendered using theme colors
- [x] 2.4 Implement hover popup documentation rendering — when `docs.summary` is present, add `<div class="glosharp-popup-docs">` below the type signature
- [x] 2.5 Implement error annotation rendering — wrap error spans in `<span class="glosharp-error-underline">`, emit `<div class="glosharp-error-message">` with code and message
- [x] 2.6 Implement highlight/focus/diff line styling — apply `glosharp-highlight`, `glosharp-focus-dim`, `glosharp-diff-add`, `glosharp-diff-remove` classes to affected lines
- [x] 2.7 Implement completion list rendering — emit `<ul class="glosharp-completion-list">` with `<li>` items after the relevant line
- [x] 2.8 Generate inline `<style>` block with theme colors, popup show/hide CSS, anchor positioning, error styling, highlight/diff styling, and `@supports not` fallback
- [x] 2.9 Implement standalone page wrapping — when `standalone: true`, wrap fragment in `<!DOCTYPE html><html><head><body>`
- [x] 2.10 Add tests for `HtmlRenderer` — verify output structure for hovers, errors, highlights, completions, standalone mode, and CSS content

## 3. CLI Render Command

- [x] 3.1 Add `render` command to `Program.cs` with argument parsing for `--theme`, `--standalone`, `--output`, plus existing options (`--framework`, `--project`, `--region`, `--no-restore`, `--cache-dir`, `--stdin`)
- [x] 3.2 Implement render command pipeline: process source → classify tokens → render HTML → write to stdout or `--output` file
- [x] 3.3 Add theme validation — reject invalid `--theme` values with error listing valid themes
- [x] 3.4 Add integration tests for `render` command — verify HTML output for a simple snippet, theme selection, standalone mode, and output file writing

## 4. Processor Integration

- [x] 4.1 Expose the `Compilation` and `SyntaxTree` from `GloSharpProcessor.ProcessAsync()` so the render command can pass them to `SyntaxClassifier` without re-compiling (add to `GloSharpResult` or return via a separate method/wrapper)
- [x] 4.2 Verify cache integration — ensure `--cache-dir` works with the render command (cached results may need classified spans stored alongside or re-classification on cache hit)
