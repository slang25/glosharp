## Why

The existing twohash integrations (Shiki transformer, Expressive Code plugin) require a JavaScript-based doc framework. Many C# developers use Hugo, Jekyll, plain HTML docs, or want quick previews without setting up a Node.js pipeline. A `twohash render` CLI command would produce self-contained HTML directly from C# source, making twohash's rich code rendering accessible everywhere with zero JS dependencies.

## What Changes

- New `render` CLI command that processes C# source and outputs self-contained HTML with syntax highlighting, hover popups, error annotations, and highlight/focus/diff styling
- Roslyn Classifier-based syntax highlighting (reuses the existing compilation — no TextMate grammars needed)
- Built-in color themes (github-dark, github-light) mapping Roslyn classification kinds to CSS colors
- CSS anchor positioning for hover popups (no JavaScript in output), with absolute-positioning fallback for older browsers
- Inline `<style>` block with all theme and popup CSS, producing embeddable HTML fragments
- Optional `--standalone` flag for full HTML page output

## Capabilities

### New Capabilities
- `html-renderer`: The render CLI command, HTML generation, CSS popup/error/highlight styles, and output format options (fragment vs standalone page)
- `syntax-classification`: Roslyn Classifier integration that maps C# tokens to themed CSS classes, with built-in theme definitions (github-dark, github-light)

### Modified Capabilities
- `cli-tool`: Adding the new `render` command with its options (`--theme`, `--standalone`, `--output`, plus existing `--project`, `--framework`, `--region`, `--cache-dir` options)

## Impact

- **Core library**: New `HtmlRenderer` class and `SyntaxClassifier` wrapper added to `TwoHash.Core`
- **CLI**: New `render` command added alongside existing `process` and `verify` commands
- **Dependencies**: No new external dependencies — Roslyn's `Classifier` API is already available via `Microsoft.CodeAnalysis.CSharp.Workspaces`
- **Output contract**: New HTML output format (the existing JSON contract is unchanged)
- **Node packages**: No changes to the Node bridge, Shiki transformer, or EC plugin
