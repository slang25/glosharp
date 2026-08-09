# GitBook Integration Feasibility

*Researched 2026-08-08. Question: what would it take for a repo already on GitBook to enable a plugin/integration and get GloSharp-enabled C# snippets?*

## TL;DR

Feasible, but not as a build-time plugin like every existing GloSharp integration. GitBook is a hosted renderer with no user-controlled markdown/build pipeline — there is no remark/rehype/Shiki hook and no raw-HTML escape hatch in content. The one supported extension point is the **Integrations platform (ContentKit)**: an integration can register a **custom block** that is triggered by a markdown code fence via Git Sync and renders through a sandboxed **webframe (iframe)**. GitBook's own **Mermaid integration** uses exactly this pattern and is the template to copy.

The hard part is GloSharp's .NET dependency: analysis requires Roslyn, which cannot run in GitBook's Workers-style integration runtime. The compute lives in two places, neither of them a server we operate: **precomputed in the repo's own CI** for readers, plus **.NET WASM inside the webframe** for live editor preview — the no-MSBuild tiers (bare snippet, `.glocontext`) are pure Roslyn and run on the browser runtime. A hosted GloSharp API was considered and dropped: it adds operated infrastructure and an arbitrary-input compute surface for no capability the other two options lack.

## What GitBook offers (verified against current docs)

- **Legacy GitBook plugins** (`gitbook-plugin-*`) belong to the long-deprecated open-source toolchain. Irrelevant; modern gitbook.com does not run them.
- **Built-in code blocks** are rendered by GitBook with Prism. No hook to customize tokenization or inject markup/CSS/JS into them.
- **No arbitrary HTML** in content. ContentKit components render a fixed vocabulary (`codeblock`, `markdown`, `card`, …); none accept raw HTML/CSS/JS. The only way to show custom-rendered output is `<webframe>` — an iframe with bidirectional postMessage.
- **Custom blocks map to code fences.** In `gitbook-manifest.yaml`:

  ```yaml
  blocks:
      - id: snippet
        title: GloSharp C# snippet
        markdown:
            codeblock: glosharp   # fence language that triggers this block
            body: content         # component prop that receives the fence body
  ```

  With Git Sync enabled, a fence like ````` ```glosharp ````` in a synced repo imports as the custom block, and blocks inserted in the editor export back as that fence. Extra fence-line attributes (```` ```glosharp propA="A" ````) become component props — a natural carrier for `--framework`, glocontext pointers, etc.
- **Integration runtime** is a serverless sandbox: `fetch` in/out, ContentKit rendering, events (`space_content_updated`, `space_gitsync_completed`, …), OAuth, secrets. No child processes, no filesystem, so no chance of running the GloSharp CLI in it.
- **The Mermaid integration** ([source](https://github.com/GitbookIO/integrations/tree/main/integrations/mermaid)) demonstrates the full shape: fence mapping (`codeblock: mermaid`), an editable-in-editor block, and a `<webframe>` whose URL is served by the integration's **own `fetch` handler** (no separate hosting needed for the frame shell); the fence content streams to the frame via `element.dynamicState('content')` + postMessage, and the frame resizes itself via `@webframe.resize`. Its manifest summary confirms the fence takeover behavior: "all code blocks with the mermaid syntax will be replaced by diagram."
- **Distribution:** GitBook CLI for local dev; publish privately/unlisted first; public marketplace listing requires submitting for GitBook review.

## Where GloSharp compute can live

| Option | How | Verdict |
|---|---|---|
| A. CI precompute + static artifacts | Repo CI runs `glosharp render` (or `process`) over all fences, publishes `sha256(code).html` fragments to static hosting (e.g. GitHub Pages); the webframe looks up by hash | **Recommended start.** No hosted compute, no cold-start, `.glocontext` makes CI SDK-free. Stale until CI runs; editor edits show plain code until pushed |
| B. .NET WASM in the iframe | GloSharp.Core + Roslyn running on the `dotnet.wasm` browser runtime inside the webframe; ref assemblies shipped as static assets | **Live editor preview.** Zero infra, zero abuse surface. Too heavy to impose on readers (~15–30 MB one-time download, seconds of first-compile latency under the interpreter) |
| ~~C. Hosted GloSharp API~~ | ASP.NET service wrapping `GloSharpProcessor`, called from the webframe | **Dropped.** Adds operated infrastructure and an arbitrary-input compute surface without enabling anything A+B don't cover |

A and B compose: readers get precomputed static HTML by hash; the editor webframe loads the WASM bundle for live preview while typing. If WASM startup latency disappoints in the editor, the degradation path is "plain code until pushed through CI" — not a hosted service.

**On WASM — decision #001 does not apply here.** That decision chose CLI-over-WASM for the *Node build-time bridge*, where spawning a process is nearly free, and its rationale ("WASM compilation would be extremely difficult") predates reality: Roslyn is pure managed IL and runs on the stock .NET browser-wasm runtime today — BlazorRepl, Telerik's C# REPL, and try.mudblazor.com all compile user C# in-browser with Roslyn. Crucially, the tiers a GitBook integration needs involve **no MSBuild and no SDK**: bare-snippet + framework refs and `.glocontext` are pure `GloSharpProcessor` + `MetadataReference`. What a browser build would need:

- **Ref loading refactor:** resolvers currently use file paths (`MetadataReference.CreateFromFile`); a browser target needs byte-array loading (`CreateFromImage`) with ref assemblies fetched as static assets. `.glocontext` pointer-refs would need a CORS-accessible package source or prebundled refs.
- **Size/latency budget:** runtime + Roslyn + BCL refs ≈ 15–30 MB compressed (one-time, cacheable); first compile takes seconds under the Mono interpreter (Roslyn is JIT-hungry; AOT trims latency but grows the bundle). Acceptable for an author editing a snippet; not acceptable per reader page-load.
- The SDK-dependent tiers (`--project` restore, `#:package` file-based apps) stay out of scope in the browser — which matches GitBook's model anyway.

#001 itself should be re-scoped ("CLI for build-time bridges" rather than "WASM is infeasible") when this work starts.

## Why the render side is already solved

`glosharp render` output is the ideal iframe payload: a self-contained `<div class="glosharp-code">` with an inline `<style>` and a **hard spec requirement of zero `<script>`/JS event attributes** (openspec/specs/html-renderer/spec.md) — all hover interactivity is CSS-only. Inside an iframe that constraint is a feature, not a limitation. The Shiki path (140-line stylesheet, CSS anchor positioning with `@supports` fallback) works too if we prefer client-side assembly from `GloSharpResult` JSON. The Expressive Code path is the wrong fit here (requires its JS module).

The existing rendering test-loop (`tests/rendering/`: fixtures → gallery → Playwright invariants, no .NET needed) can validate the iframe shell — especially the viewport-containment invariant, since popups cannot escape iframe bounds.

## What would need to be built

1. **`@glosharp/gitbook` integration** (TypeScript, GitBook CLI project):
   - manifest with the `markdown.codeblock` block mapping;
   - ContentKit component: editable `codeblock` in edit mode, `webframe` preview otherwise (Mermaid pattern);
   - `fetch` handler serving the iframe shell HTML + resolving code→rendered HTML by static-hash lookup, with runtime caching.
2. **CI story (option A):** a reusable GitHub Action / script — scan synced markdown for the fence, batch through `processGloSharpBlocks()` or `glosharp render`, publish hash-keyed artifacts. `.glocontext` committed in the repo supplies compilation context cheaply.
3. **Browser build of GloSharp.Core (option B)** for live editor preview: wasm target, `CreateFromImage`-based ref loading, ref assemblies as static assets.
4. **Docs + marketplace submission** (private install works immediately; public listing needs GitBook review).

## Open questions (need a prototype to answer)

- **Can the fence be `csharp`?** The manifest takes any string, and Mermaid proves fence takeover works for its own syntax — but whether GitBook allows claiming a built-in Prism language like `csharp` (and doing so for *all* C# fences in installed spaces, wanted or not) is unverified. Safe default: a dedicated `glosharp` fence, which also keeps snippets valid on other renderers.
- **Webframe sizing:** `aspectRatio` is required; Mermaid works around it with postMessage resize. Hover popups need headroom — likely render with padding or grow-on-hover.
- **Search/AI indexing:** the raw fence content lives in GitBook's content model (as block props), so it should index like Mermaid source does — worth confirming the rendered hover content is acceptable to lose from search.
- **Multi-fence pages:** one iframe per snippet; measure weight on snippet-heavy pages (each shell is tiny and cacheable, but N iframes is N documents).
- **WASM editor-preview latency:** with the hosted API dropped, the editor experience rides entirely on `dotnet.wasm` startup + first-compile time. Measure download-to-first-hover on a cold cache; if it's poor, the accepted degradation is plain code in the editor until CI runs, so quantify how bad that feels before shipping.

## Sources

- https://gitbook.com/docs/developers/integrations/guides/markdown.md (fence ↔ custom block mapping)
- https://gitbook.com/docs/developers/integrations/development/contentkit.md and `.../contentkit/reference.md` (components; no arbitrary HTML; webframe)
- https://gitbook.com/docs/developers/integrations/development/runtime.md (sandboxed runtime, fetch, events)
- https://github.com/GitbookIO/integrations/tree/main/integrations/mermaid (manifest + webframe pattern)
- https://gitbook.com/docs/creating-content/blocks/code-block (built-in Prism code blocks)
