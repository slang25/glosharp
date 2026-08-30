## Context

`research/07-gitbook-integration.md` verified the platform constraints against current GitBook docs and against GitBook's own Mermaid integration:

- Legacy `gitbook-plugin-*` is dead; modern gitbook.com does not run it.
- Built-in code blocks render through Prism with no hook to customise tokenisation or inject markup/CSS/JS.
- ContentKit renders a fixed component vocabulary. None of it accepts raw HTML. The only way to show custom-rendered output is `<webframe>` — an iframe with bidirectional `postMessage`.
- A custom block can be bound to a fence language in the manifest (`markdown.codeblock: <lang>`, `markdown.body: <prop>`); with Git Sync, matching fences import as that block and editor-inserted blocks export back as that fence. Extra fence-line attributes arrive as component props.
- The integration runtime is a serverless sandbox: `fetch` in/out, ContentKit rendering, events, OAuth, secrets. No child processes, no filesystem — so no `glosharp` CLI, and no Roslyn.
- Mermaid demonstrates the full shape end to end: fence mapping, an editable-in-editor block, a `<webframe>` whose URL is served by the integration's own `fetch` handler, content streamed to the frame via `element.dynamicState(...)` + `postMessage`, and self-resizing via `@webframe.resize`.

The compute decision (CI precompute; WASM deferred; hosted API rejected) is recorded as decision 006 and is not re-argued here.

## Goals / Non-Goals

**Goals:**
- A `glosharp` fence in a Git Sync repo renders with real hover information for readers, with no infrastructure to operate.
- CI → browser agreement rests on the snippet text alone (content addressing), with one shared definition of the canonical form.
- Both ways a lookup can come up empty (nothing configured, nothing published) degrade to readable plain code with an explanation, never a blank frame.
- Popups are fully visible inside the frame, and that is asserted in a real browser.
- The artifact set is deterministic and diffable, so `--check` can gate a pull request.

**Non-Goals:**
- Live editor preview. Until Roslyn runs in the browser, an author sees plain code until CI publishes. Decision 006 records why this is deferred rather than solved with a service.
- Claiming the `csharp` fence.
- A batch `glosharp render` command. One CLI process per (snippet, theme) with bounded concurrency is enough at documentation scale; see "Rendering cost" below.
- Public GitBook marketplace listing. Private/unlisted install works immediately; listing needs GitBook review and an organization we do not have.

## Decisions

### 1. The artifact key covers the code and nothing else

`sha256(canonicalizeSnippet(body))`, where canonicalisation is CRLF/CR → LF plus dropping leading and trailing blank space.

The temptation is to fold the fence attributes into the key so that `framework="net9.0"` and `framework="net10.0"` cannot collide. Rejected: it makes the key a function of the block's props as well as its body, which the webframe would have to reconstruct exactly, and props are the part most likely to be reformatted by GitBook's editor. Instead the builder *detects* the collision — two byte-identical snippets asking for different frameworks is a hard error naming both locations — so the failure mode is a build error rather than a silent mis-render.

Canonicalisation is deliberately minimal for the same reason. Trimming trailing whitespace on interior lines would be more robust to editor round-tripping but can change the value of a raw string literal.

### 2. One definition of the canonical form, serialised into the shell

The shell script embeds `canonicalizeSnippet` via `Function.prototype.toString()` rather than restating it in JavaScript. A drift between the two would not throw — it would silently miss every lookup and degrade to plain code, which is exactly the failure a reader cannot report usefully. The function is therefore constrained to be self-contained (no imports, no downlevel helpers), and a test evaluates the serialised text and compares its behaviour, and its hash, against the original.

### 3. Themes are directories, not part of the key

`<out>/<theme>/<key>.html`. The shell resolves the theme from the block's `theme` prop, then the space configuration, then `prefers-color-scheme`, and re-fetches when the reader's colour scheme changes.

The cost is rendering each snippet twice by default. The alternative — one artifact plus client-side recolouring — is not available: `glosharp render` inlines theme colours into the fragment, which is what makes it self-contained. `--theme` narrows this to one theme for anyone who wants to pay less.

### 4. Lookup runs in the frame, not in the integration's `fetch` handler

The `fetch` handler serves only the shell, identical bytes for every request, cacheable for a day.

Resolving artifacts worker-side was considered: it would allow Workers-level caching and would avoid needing CORS on the artifacts host. It was rejected because the artifacts base URL would have to reach the handler as a request parameter (the public content endpoint carries no installation context), which turns the integration into an open GET proxy that inlines arbitrary fetched HTML onto its own origin. Fetching from the frame costs a CORS requirement instead — satisfied by GitHub Pages, the recommended host, and by any static host worth using.

### 5. Popups open downward, and the frame grows on hover

The renderer positions popups with `position: fixed; position-area: top`. Inside a webframe that clips: a popup above a token on the first line has nowhere to go, and the frame cannot paint outside its own box.

Growing the frame *upward* would mean adding top padding on hover, which moves the token out from under the pointer — hover is lost, the frame shrinks, the pointer is back over the token, and it oscillates. So the shell overrides the popup to open downward (including in the no-anchor-support fallback) and asks the host for extra height while a popup is open. Content position never changes, so hover is stable. A popup that would run off the right edge is nudged back with a `translate`.

The alternative — permanent headroom sized to the tallest popup — wastes vertical space on every snippet on the page, hovered or not.

The pointer-out shrink is debounced (80 ms) and `popupFor` treats the popup itself as part of its hover target, so moving the pointer onto a popup (which the renderer's CSS keeps open) does not shrink the frame out from under it.

### 6. `render()` on the Node bridge rather than spawning from the new package

The builder needs `glosharp render`, and the bridge already owns executable discovery (`glosharp` on PATH, else `dotnet glosharp`), the option surface, and the "install it like this" error message. Duplicating that in a second package would mean two places to keep in step. `spawnCli` now returns raw stdout and `process()` parses the JSON, so `render()` can return HTML through the same path. The two caches stay separate: the same code can be both a `GloSharpResult` and an HTML string.

### 7. Rendering cost

Every `glosharp render` is a fresh process, so nothing is shared across snippets — `CompilationContextCache` is in-process, and the disk `ResultCache` does not cover the classification and rendering that `render` adds. 50 snippets × 2 themes at ~1.5 s each is ~20 s wall clock at the default concurrency of 4, and higher `--concurrency` scales it down.

A batch `render` command in the .NET CLI would resolve references once for the whole run and would be the right fix if this becomes a bottleneck. It is not built here: it is a change to the .NET side for a cost that is currently acceptable, and `--skip-existing` plus a cached output directory already makes repeat CI runs nearly free.

### 8. Testing the shell without .NET and without GitBook

The rendering feedback loop's premise is that browser-level behaviour is verified from committed fixtures with no .NET SDK. The frame page keeps that: `scripts/gitbook-host.js` implements GitBook's side of the webframe contract (answer `@webframe.ready` with the block's `data`, apply every `@webframe.resize`) and nothing else, and the "published" artifacts are Shiki-path renders with the stylesheet inlined — same class names, same anchor positioning, same self-containment as `glosharp render` output.

The unpublished-snippet case gets its own page because a missing artifact necessarily 404s, and the suite's console-cleanliness fixture treats a console error as a page defect. Rather than weaken that fixture globally, it gained an opt-in `consoleErrorAllowlist` that one spec narrows for one pattern.

## Risks / Trade-offs

- **Unverified against a live GitBook space.** The manifest and the bundled script are validated by `gitbook integration check` in CI, and the shell is validated in a browser against a faithful stub of GitBook's message contract, but nothing here has been installed on a real space. The open questions the research flagged — whether `aspectRatio` plus resize behaves as Mermaid suggests on snippet-heavy pages, how N iframes feel, whether fence attributes arrive as props exactly as documented — need one real install to close.
- **Staleness is visible.** An author editing a snippet in GitBook sees plain code with a note. This is the accepted degradation, not a defect, but it is the main thing that will feel wrong in use.
- **One iframe per snippet.** Each shell is small and cacheable; N snippets is still N documents.
- **Search/AI indexing** covers the fence body (it lives in the content model as a block prop) but not the rendered hover text.
