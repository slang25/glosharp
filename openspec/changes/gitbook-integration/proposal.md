## Why

`research/07-gitbook-integration.md` established that a repo already on GitBook cannot get Glo#-enabled snippets the way every other integration works. GitBook is a hosted renderer: no remark/rehype hook, no Shiki transformer, no raw HTML in content, and the one extension point — a ContentKit integration — runs in a Workers-style sandbox with no child processes and no filesystem. Roslyn cannot run there.

The research also established the shape of the answer, copied from GitBook's own Mermaid integration: a custom block bound to a code fence, rendered through a sandboxed webframe, with the compute moved to the repo's own CI and the result published as content-addressed static HTML. That is what this change builds.

## What Changes

- **New `@glosharp/gitbook` package** carrying both halves of the integration:
  - a GitBook integration (`gitbook-manifest.yaml` + ContentKit component) that claims the `glosharp` fence, shows an editable code block in the editor and a webframe to readers;
  - a `glosharp-gitbook` CLI that scans Markdown for `glosharp` fences and publishes one pre-rendered fragment per (theme, snippet) as `<out>/<theme>/<sha256>.html` plus a deterministic `index.json`;
  - a composite GitHub Action wrapping the CLI.
- **Content addressing is the whole contract.** The webframe hashes the fence body it was handed and fetches the matching fragment. CI and the reader's browser agree on nothing else — no manifest to keep in sync, no per-page coordination. The canonicalisation before hashing (CRLF → LF, leading/trailing blank space dropped) is defined once and shared: the shell script carries the same function, serialised, so the two sides cannot drift.
- **The webframe shell supplies what the fragment cannot.** `glosharp render` output is script-free by spec, but an iframe has no intrinsic height and cannot paint outside its own box. The shell measures and reports height, flips popups to open downward, and asks GitBook for extra height while one is open.
- **`render()` on the Node bridge**, so the CLI drives `glosharp render` through the same executable discovery and option surface as `process()` instead of reimplementing it.
- **A local preview.** `glosharp-gitbook dev` serves the real shell, the real artifacts, and a host page implementing GitBook's webframe contract, so snippets can be seen exactly as the block will show them without a GitBook account. Reloading re-scans and re-renders only what changed.
- **Rendering feedback loop covers the frame — and the standalone renderer.** A `gitbook-frame` gallery page drives the real shell through GitBook's message contract over real `glosharp render` artifacts, and `standalone-{dark,light}` pages render those artifacts directly. Still no .NET at test time: `fixtures:update` commits the HTML alongside the existing JSON.
- **Two defects fixed in the standalone renderer**, both found by the preview and neither reachable through the Shiki or EC pages. Popups were emitted after `</code></pre>` while the CSS revealed them with an adjacent-sibling selector, so no popup could ever open; and every fragment numbered its anchors from `--th-0`, so on a page with several snippets each popup anchored to the last fragment's token. `glosharp render` output — the bytes a GitBook reader sees — had no working hovers before this.
- **Decision 001 re-scoped, decision 006 added.** 001 rejected WASM partly on feasibility grounds that were never true; it is now scoped to build-time bridges. 006 records the GitBook compute decision, including why a hosted GloSharp API was rejected and why lookup happens in the browser rather than the integration's `fetch` handler.

## Capabilities

### New Capabilities
- `gitbook-integration`: the manifest's fence↔block mapping, the ContentKit component's editor/reader split, the webframe shell's message contract, artifact lookup and its two degradation paths, popup containment inside the frame.
- `gitbook-artifacts`: fence discovery in Markdown, the canonical snippet key, the published artifact layout and `index.json`, and the `glosharp-gitbook` CLI (`build`, `scan`, `--check`, `--prune`, `--skip-existing`).

### Modified Capabilities
- `html-renderer`: popups render inside their hover span (the show selector becomes `>`); anchor names are prefixed per fragment from a hash of the code, with `anchor-scope: all` on the wrapper.
- `render-fixtures`: `glosharp render` HTML committed per sample and theme, covered by the same drift and orphan checks.
- `node-bridge`: adds `render(options)` returning the HTML from `glosharp render`, with `theme`/`standalone` on top of the shared process options and a cache keyed by code plus rendering arguments.
- `rendering-gallery`: adds the `gitbook-frame` pages, the frame shell, and hash-keyed artifacts published into the gallery.
- `rendering-invariants`: adds the webframe invariants, and lets a spec narrow the console-cleanliness fixture when the behaviour under test *is* a browser-level failure.

## Impact

- `packages/gitbook`: new package (published to npm for the CLI, published to GitBook for the integration).
- `packages/glosharp`: `render()` added; `spawnCli` now returns raw stdout and JSON parsing moved to `process()`.
- `tests/rendering`: new gallery pages (`standalone-*`, `gitbook-frame*`), committed HTML fixtures, and two specs; `helpers.ts` gains a `consoleErrorAllowlist` option. All committed HTML fixtures change with the renderer fix.
- CI: the Node job builds the package, runs its tests, typechecks the integration, and validates the manifest; the web-rendering job builds the package so the gallery can import it.
- `src/GloSharp.Core`: `HtmlRenderer` popup nesting and anchor scoping (see the `html-renderer` delta); no other .NET changes. Live editor preview (Roslyn on `dotnet.wasm`) is explicitly out of scope — see design.md.
