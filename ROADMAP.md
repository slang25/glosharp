# Twohash Roadmap

Potential next features for twohash, roughly ordered by impact. Each item is self-contained and suitable for an `/opsx:propose` session.

## ~~1. XML Documentation Enhancements~~ ✅

Done. `ExtractDocComment()` now extracts `<summary>`, `<param>`, `<returns>`, `<remarks>`, `<example>`, and `<exception>` tags into a structured `TwohashDocComment` object. Propagated through JSON output, Node bridge types, and EC popup rendering. Framework and NuGet XML doc files are now loaded via `XmlDocumentationProvider`.

## ~~2. Directive Markers (@highlight, @focus, @diff)~~ ✅

Done. MarkerParser now recognizes `// @highlight`, `// @focus`, and `// @diff: +/-` directives with bare (next-line) and range (`N-M`) targeting. `TwohashResult.Highlights` is populated with `TwohashHighlight` entries (`line`, `character`, `length`, `kind`). Propagated through JSON output, Node bridge types (`TwohashHighlight` interface), and EC plugin rendering with theme-aware CSS for highlight backgrounds, focus dimming, and diff add/remove coloring.

## ~~3. File-Based Apps (.NET 10)~~ ✅

Done. `FileDirectiveParser` recognizes `#:package`, `#:sdk`, `#:property`, and `#:project` directives, strips them from rendered output, and preserves them in `original`. `FileBasedAppResolver` delegates to the .NET SDK (`dotnet build <file.cs>` + `--getProperty`) for NuGet resolution, reusing `ProjectAssetsResolver` for the generated `project.assets.json`. Auto-detected when source contains `#:` directives and no `--project` flag is provided. SDK version >= 10.0 is validated. `meta.packages` populated from directives, `meta.sdk` field added. Propagated through JSON output and Node bridge types.

## ~~4. Incremental/Cached Compilation~~ ✅

Done. Two-layer caching eliminates redundant work for documentation sites with many snippets. `CompilationContextCache` reuses resolved `MetadataReference[]` arrays in-process (keyed by framework, packages, project assets hash) — the `verify` command resolves references once for all files sharing the same project. `ResultCache` provides disk-based caching via `--cache-dir <path>`: full `TwohashResult` JSON cached by SHA256 of (twohash version, framework, packages, project path, source code). Cache hits skip all Roslyn work. Atomic writes prevent corruption from concurrent CLI processes. Corrupt cache files are treated as misses. Propagated through CLI (`--cache-dir` on `process` and `verify`) and Node bridge (`cacheDir` option in `TwohashOptions` and `TwohashProcessOptions`).

## ~~5. Standalone HTML Renderer~~ ✅

Done. `twohash render` CLI command outputs self-contained HTML with syntax highlighting, hover popups, error annotations, completion lists, and highlight/focus/diff styling — no Shiki or EC dependency needed. `SyntaxClassifier` wraps Roslyn's `Classifier.GetClassifiedSpansAsync()` for token classification (keywords, types, strings, comments, etc.) with deduplication of syntactic vs semantic spans. `HtmlRenderer` generates inline `<style>` with CSS anchor positioning for hover popups (`--th-N` anchors), `@supports not` fallback for older browsers, and theme-aware coloring. Two built-in themes: `github-dark` and `github-light`. Supports `--standalone` (full HTML page), `--output <path>` (file output), and `--theme <name>`. Propagated through CLI (`render` command) and core (`SyntaxClassifier`, `HtmlRenderer`, `TwohashTheme`, `TwohashProcessResult`).

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
