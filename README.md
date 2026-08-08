# Glo#

**twoslash for C#** — extract rich symbol metadata from C# code using Roslyn, for rendering beautiful code snippets in docs, blogs, and slides.

## The problem

TypeScript has [twoslash](https://twoslash.netlify.app/), which extracts compiler-derived type information and renders it inline in code snippets. C# has nothing equivalent. Documentation authors either manually annotate code or ship plain syntax-highlighted blocks with no type information.

## The vision

Run `glosharp` against C# code and get:
- **Hover information** — type signatures, XML doc comments, just like VS/VS Code tooltips
- **Compile verification** — fail in CI if the code doesn't compile
- **Structured metadata** — JSON output that integrations can consume
- **Beautiful rendering** — via Expressive Code plugin, Shiki transformer, or standalone HTML/CSS

## Research questions

1. How does twoslash work internally? What's the data model? → [01-twoslash-architecture](research/01-twoslash-architecture.md)
2. What Roslyn APIs do we need for symbol metadata extraction? → [02-roslyn-metadata-extraction](research/02-roslyn-metadata-extraction.md)
3. How do Shiki transformers and Expressive Code plugins work? → [03-shiki-and-expressive-code](research/03-shiki-and-expressive-code.md)
4. How do we resolve NuGet packages for compilation? → [04-nuget-resolution](research/04-nuget-resolution.md)
5. How do people manage code snippets in docs today? → [05-code-snippet-management](research/05-code-snippet-management.md)
6. What doc/blog frameworks should we target? → [06-doc-framework-landscape](research/06-doc-framework-landscape.md)

## Design

- [Architecture](design/architecture.md) — system components and data flow
- [Data format](design/data-format.md) — output metadata JSON schema
- [Integration points](design/integration-points.md) — Shiki, Expressive Code, standalone renderer
- [Decisions](design/decisions.md) — key decisions log

## Portable compilation context (`.glocontext`)

`glosharp compact-complog <input.complog> -o <out.glocontext>` compacts a Basic.CompilerLog `.complog` into a small, git-friendly `.glocontext` file that captures just what Glo# needs to resolve types, hovers, and completions.

The compactor:

- replaces framework reference assemblies with *pointers* into their NuGet targeting packs (`microsoft.netcore.app.ref`, `microsoft.aspnetcore.app.ref`, …) — a pointer records the pack, the file path inside it, and a SHA-256 of the canonical bytes, so nothing framework-shaped is stored in the file at all;
- rewrites the remaining (NuGet/library) reference assemblies with [JetBrains.Refasmer](https://github.com/JetBrains/Refasmer) so only public API metadata is retained (method bodies, private types, and internals are stripped);
- drops analyzer DLLs, original source text, and generated source text — they are not needed for symbol-only rendering;
- deduplicates identical post-refasm references across compilations and stores each one once by SHA-256;
- writes a `GLOCTX`-magic header followed by a zstd-compressed tar containing a deterministic `manifest.json` plus `refs/<hash>.dll` blobs.

Typical results: a ~6 MB BCL-only complog shrinks to **~9 KB**, and a ~15 MB ASP.NET + EF complog to **~460 KB**. Output is byte-deterministic, so a checked-in `.glocontext` only changes when the underlying compilation context actually changes. The `--complog` flag on `process`, `verify`, and `render` auto-detects either format by magic bytes.

When a pointer-bearing (format v2) `.glocontext` is opened, the referenced packs are located through: the NuGet global packages folder (`NUGET_PACKAGES` or `~/.nuget/packages`), then a glosharp-managed cache (`GLOSHARP_CACHE_DIR` or the platform local-app-data dir), then a one-time ~7 MB download per pack from nuget.org (cached thereafter). Every pointed file is verified against its recorded SHA-256 before use. On a machine that has ever restored a project targeting the same framework, resolution needs no network at all.

Because the same pack version can ship byte-different files across distribution channels (the installed SDK's copies differ from nuget.org's in signing, and some facades are separate builds), the compactor *canonicalizes*: each framework reference is matched to the nuget.org-channel file (by content hash, then MVID, then file name + assembly version) and the pointer records those canonical bytes. Producer and consumer therefore agree byte-for-byte regardless of which SDK produced the complog.

For artifacts that must resolve fully offline with no pack acquisition, pass `--self-contained` — every reference is embedded as a blob (format v1), at the cost of the old ~1–3 MB sizes. If the canonical packs cannot be acquired at compact time, the compactor warns and falls back to embedding those references.

> The zstd codec is provided by [`ZstdSharp.Port`](https://www.nuget.org/packages/ZstdSharp.Port/) today. Once .NET 11 ships, the codec will be swapped for `System.IO.Compression`'s built-in zstd with no format change.
>
> The file header reserves baseline id/version slots for a possible future format that layers `zstd --patch-from` over a shipped baseline artifact. Readers **must** reject any file with non-zero baseline fields so such output does not silently resolve on an older reader.

## Testing the web rendering

Browser-level rendering quality (popup positioning, scroll tracking, themes,
mobile viewports) is verified by the [rendering feedback loop](tests/rendering/README.md):
committed `GloSharpResult` fixtures feed a deterministic gallery of every
feature state across both render paths, and a Playwright suite asserts
geometry/behavior invariants in Chromium and Firefox — no .NET SDK needed.

```sh
npm run gallery:build -w tests/rendering && npm run gallery:serve -w tests/rendering  # browse it
npm test -w tests/rendering                                                           # run the invariants
```

## References

- [Annotated links](references/links.md)
