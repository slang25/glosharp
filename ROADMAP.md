# Twohash Roadmap

Potential next features for twohash, roughly ordered by impact. Each item is self-contained and suitable for an `/opsx:propose` session.

## ~~1. XML Documentation Enhancements~~ ✅

Done. `ExtractDocComment()` now extracts `<summary>`, `<param>`, `<returns>`, `<remarks>`, `<example>`, and `<exception>` tags into a structured `TwohashDocComment` object. Propagated through JSON output, Node bridge types, and EC popup rendering. Framework and NuGet XML doc files are now loaded via `XmlDocumentationProvider`.

## ~~2. Directive Markers (@highlight, @focus, @diff)~~ ✅

Done. MarkerParser now recognizes `// @highlight`, `// @focus`, and `// @diff: +/-` directives with bare (next-line) and range (`N-M`) targeting. `TwohashResult.Highlights` is populated with `TwohashHighlight` entries (`line`, `character`, `length`, `kind`). Propagated through JSON output, Node bridge types (`TwohashHighlight` interface), and EC plugin rendering with theme-aware CSS for highlight backgrounds, focus dimming, and diff add/remove coloring.

## 3. File-Based Apps (.NET 10)

.NET 10 introduces `#:package`, `#:sdk`, and `#:property` directives for single-file apps. Twohash should recognize these, use them for compilation (resolve NuGet packages, set SDK properties), and strip them from rendered output. This would let a single `.cs` file be both a valid runnable app and a twohash input without needing a `.csproj`.

**Scope**: MarkerParser (recognize `#:` directives) + new resolver for `#:package` NuGet references + TwohashProcessor integration + CLI passthrough.

## 4. Incremental/Cached Compilation

For documentation sites with many snippets from the same project, the current approach recompiles from scratch each time. Add workspace-level caching: reuse the `CSharpCompilation` when the same project references are used, and add a `--cache-dir` CLI option for persisting compiled state across builds.

**Scope**: TwohashProcessor caching layer + CLI `--cache-dir` option + Node bridge passthrough.

## 5. Standalone HTML Renderer

A `twohash render` CLI command that outputs self-contained HTML with syntax highlighting, hover popups, and error annotations — no Shiki or EC dependency needed. Useful for Hugo, Jekyll, plain HTML docs, or quick previews. Needs a syntax highlighting strategy (Roslyn's classifier or bundled TextMate grammars).

**Scope**: New CLI command + HTML template + CSS bundle + syntax highlighting integration.

## 6. Shiki Async DX Improvements

Shiki's `preprocess` hook is synchronous, forcing users to pre-compute twohash results before calling `codeToHtml`. Add a batch processing helper (`processTwohashBlocks(codeBlocks, options)`) and a result-map-based transformer that looks up pre-computed results by code hash, making the typical usage pattern cleaner.

**Scope**: Shiki package only — new helper functions + updated transformer + tests + docs.

## 7. Language Version & Nullable Control

Support per-snippet configuration via markers:
- `// @langVersion: 12` — set C# language version
- `// @nullable: enable|disable` — control nullable context

Currently hardcoded to `LanguageVersion.Latest` and `NullableContextOptions.Enable`. These markers would be parsed, stripped from output, and applied to `CSharpParseOptions`/`CSharpCompilationOptions`.

**Scope**: MarkerParser + TwohashProcessor options + tests.

## 8. EditorConfig / Project Defaults

Support a `.twohashrc` or `twohash.config.json` file for project-wide defaults (framework, project path, region, cache settings) so every CLI call doesn't need `--framework net9.0 --project ./MyProject.csproj`. CLI args override config file values.

**Scope**: Config file parsing + CLI integration + Node bridge config option.

## 9. Portable Compilation (complog)

Accept `.complog` artifacts (Roslyn's portable compilation format) so documentation builds don't need the full SDK + NuGet cache. A CI step runs `dotnet build` and produces the complog; twohash consumes it for accurate type resolution. Referenced in design decisions (decision 003).

**Scope**: New `--complog` option + complog parser + TwohashProcessor integration.

## 10. Richer Error Display

Enhance error rendering with:
- Quick-fix suggestions from Roslyn's `CodeFixProvider`
- Warning severity icons/colors (distinct from errors)
- Clickable error codes linking to MS docs
- Multi-line error spans

**Scope**: Core extraction + models + Shiki/EC rendering.
