## 1. Node bridge

- [x] 1.1 Split `spawnCli` into raw-stdout invocation with JSON parsing moved to `process()`; extract shared argument building
- [x] 1.2 Add `render(options)` returning HTML, with `theme`/`standalone` and a separate cache keyed by source + rendering arguments
- [x] 1.3 Export `GloSharpRenderOptions` (and the previously unexported `GloSharpProcessOptions`)
- [x] 1.4 Tests: command shape, theme/standalone forwarding, shared option surface, per-theme cache keys, cache independence from `process()`, non-zero exit

## 2. Snippet key and fence scanning

- [x] 2.1 `canonicalizeSnippet` — CRLF/CR → LF, drop leading/trailing blank space, nothing else; self-contained so it can be serialised
- [x] 2.2 `snippetKey` (node SHA-256) and `isSnippetKey`
- [x] 2.3 `findFences` — CommonMark fence extents (length, indentation stripping, tilde fences, backtick info strings, unterminated fences), case-insensitive language match, 1-based line numbers
- [x] 2.4 `parseFenceAttributes` — quoted/unquoted/bare, last-one-wins
- [x] 2.5 `collectMarkdownFiles` — files and directories, `.md`/`.mdx`, skip `node_modules`/`.git`/`.gitbook`, sorted and deduplicated
- [x] 2.6 Tests for all of the above, including the nested-fence and indented-fence cases

## 3. Artifact builder

- [x] 3.1 `collectSnippets` — dedupe by key, record every occurrence with POSIX-separator paths, error on conflicting `framework` for one key
- [x] 3.2 `buildArtifacts` — `<theme>/<key>.html` per (theme, snippet), bounded concurrency, changed/unchanged classification against disk
- [x] 3.3 Deterministic `index.json` (sorted keys/themes/occurrences, `framework` omitted when absent)
- [x] 3.4 `check`, `skipExisting`, `prune`, orphan reporting (every directory under the out dir, so dropping a theme surfaces it), per-(key, theme) failure collection
- [x] 3.5 Tests with an injected fake renderer: layout, verbatim body, byte-identical rebuild, reuse, check-writes-nothing, stale drift, skip-existing, orphans/prune, failure collection, one render per (snippet, theme)

## 4. CLI and action

- [x] 4.1 `glosharp-gitbook build` / `scan` with the documented flags; unknown option and missing value are errors
- [x] 4.2 Exit non-zero on render failure or on `--check` drift
- [x] 4.3 `bin.js` entry point separate from the testable `run()`
- [x] 4.4 Composite GitHub Action wrapping the CLI (`if` blocks rather than `[ … ] && …`, which would fail the step under `set -e`)
- [x] 4.5 Tests for argument parsing
- [x] 4.6 End-to-end check against the real CLI: scan and build a sample document, confirm hash-keyed fragments with hover markup

## 5. Webframe shell

- [x] 5.1 `renderFrameShell(options)` — pure, deterministic, single script element, `color-scheme: light dark`
- [x] 5.2 GitBook contract: `@webframe.ready`, `{ state }` message handling, `@webframe.resize` with aspect ratio and height, re-measure on **width** change only (the host applying our own resize raises a resize event; re-measuring on that collapsed the frame under an open popup)
- [x] 5.3 Theme resolution (state → `prefers-color-scheme`), re-fetch on colour-scheme change, discard superseded responses
- [x] 5.4 Artifact fetch and injection; escaped plain-code fallback with a distinct note for "not configured" and "not published"
- [x] 5.5 Popups forced downward (including the no-anchor fallback); grow-on-hover with right-edge nudge; debounced shrink; popup counts as part of its hover target
- [x] 5.6 Serialise `canonicalizeSnippet` into the script from one definition, with a test that evaluates the serialised copy and compares behaviour and hash
- [x] 5.7 Tests for shell invariants and `normalizeArtifactsUrl`

## 6. GitBook integration

- [x] 6.1 `gitbook-manifest.yaml` — `snippet` block bound to the `glosharp` fence, space configuration for `artifactsUrl` and `theme`, organization from an env variable
- [x] 6.2 ContentKit component — editable `codeblock` with the frame as footer, frame alone for readers, webframe `data` carrying dynamic content plus artifacts URL and theme
- [x] 6.3 `fetch` handler serving the shell with a public cache max-age and no proxying
- [x] 6.4 `normalizeArtifactsUrl` in a plain module so it is shared and testable
- [x] 6.5 Type-check the integration with GitBook's own bundler-style config, placed beside the sources so editors pick it up
- [x] 6.6 `gitbook integration check` passes

## 7. Rendering feedback loop

- [x] 7.1 Publish the real shell and hash-keyed artifacts into the gallery (committed `glosharp render` HTML fixtures — see 9.5)
- [x] 7.2 `gitbook-frame` page with one iframe per fixture plus the no-artifacts-URL case; separate page for the deliberately-unpublished case
- [x] 7.3 A host stub implementing GitBook's webframe contract and nothing more (now `GITBOOK_HOST_SCRIPT`, shared with the package's preview — see 8.1)
- [x] 7.4 `consoleErrorAllowlist` option on the shared fixture, used by exactly one spec for exactly one pattern
- [x] 7.5 `specs/gitbook-frame.spec.ts` — ready/sizing, hash lookup, height fidelity, containment for every popup, growth exercised, growth not undone by the host applying it (verified to fail without the width guard), shrink-back, both degradation paths
- [x] 7.6 Add the frame page to the page-load smoke list; full suite green in Chromium and Firefox

## 8. Local preview

- [x] 8.1 `GITBOOK_HOST_SCRIPT` — GitBook's side of the webframe contract as one shared definition, used by both the preview and the gallery (the gallery's private copy deleted)
- [x] 8.2 `renderDevHost` / `renderWebframeIframe` / `renderDevHostError` — standalone preview page with attribute-safe state embedding
- [x] 8.3 `startDevServer` — `/` rebuilds then renders the host page, `/frame` serves the shell, `/artifacts/*` serves the built fragments and nothing outside them
- [x] 8.4 `glosharp-gitbook dev` with `--port`, `--frame-theme`, `--out`, `--fresh`, `--no-build`; artifacts default to a stable per-project temp directory; SIGINT closes cleanly
- [x] 8.5 Reject an `--executable` path that does not exist, before rendering
- [x] 8.6 `sample/preview.md` and `npm run dev:frame` so the repo has a zero-argument preview
- [x] 8.7 Reserve an estimated height per frame so loading does not reflow the page (measured CLS 0.28 → 0.003), and give the frame a visible boundary — the whole point of the page is judging whether a popup fits inside it
- [x] 8.8 Tests for the host page, the iframe escaping, and the server (render-on-request, reload picks up edits, build failure surfaces, traversal refused, `--no-build`)

## 9. Standalone renderer defects found by the preview

- [x] 9.1 **Popups could never open.** `HtmlRenderer` emitted every popup after `</code></pre>` while the CSS revealed them with `.glosharp-hover:hover + .glosharp-popup` — an adjacent-sibling selector that cannot cross parents. Popups now render inside their hover span and the selector is `>`.
- [x] 9.2 **Anchor names collided between fragments.** Every fragment numbered from `--th-0`, and anchor names are document-global, so on a page with several snippets every popup anchored to the last fragment's token. Names are now prefixed with a hash of the code (deterministic, so fixtures and artifacts stay byte-stable) and `.glosharp-code` declares `anchor-scope: all`.
- [x] 9.3 **Code was mangled inside `<pre>`.** Surfaced by looking at the preview rather than at assertions. `RenderPopup` ended with `AppendLine`, so once the popup moved inside the `<pre>` its trailing newline broke the line after every hover token (fixed: `Append`). Separately, the block had two line-break sources at once — `display: block` line spans *and* the real newlines between them — double-spacing every block. Establishing which to drop needed a browser experiment: Chromium serialises a block boundary as a newline and Firefox serialises it as nothing, so dropping the newlines makes code copy out of Firefox as one run-on line, and keeping both doubles Chromium's clipboard newlines. Lines are now `display: inline` with the real newlines as the single source — the Shiki path's model. Accepted cost: a line-level background ends with the text instead of spanning the block; a test pins that it is still rendered.
- [x] 9.4 .NET regression tests: popup nested inside its anchor and inside the code block; anchor prefixes differ between snippets; rendering is deterministic
- [x] 9.5 Root cause was a coverage gap — the gallery only ever rendered the Shiki and EC paths. `fixtures:update` now also commits `glosharp render` HTML per theme, the gallery has `standalone-{dark,light}` pages, and `specs/standalone.spec.ts` asserts popups open, sit next to their token, close, stay in the viewport, ship no scripts, and — the assertions that would have caught 9.3 — that lines are single-height, single-spaced, and copy back out as the original source.
- [x] 9.6 The GitBook frame page now publishes those same HTML fixtures as its artifacts, so the webframe specs run against the real production combination instead of a Shiki stand-in.

## 10. Webframe defects found by driving it in a browser

- [x] 10.1 **Popups sat on top of the code.** `growFor` derived the needed height from the popup's current rect — but a popup taller than the space below its token is slid upward by the browser to stay in the viewport, so it always measured as "already fitting", the frame never grew enough, and the popup parked over the lines it was explaining. Height now derives from the anchor. The containment assertion passed throughout; the spec gained "opens below its own token", verified to fail without the fix.
- [x] 10.2 **A frame could load blank.** The shell announced `@webframe.ready` exactly once. Against a host whose listener attaches late — a deferred script, a hydrating page — the message lands on nothing and the frame is never sent state. Reproduced as a ~1-in-7 flake in the suite (`data-resizes="1"`, `data-height="1"`, `data-ready` unset). The shell now re-announces until state arrives, and both hosts attach their listener before any frame is parsed.
- [x] 10.3 **A box inside a box.** The shell padded its body on top of the fragment's own padding, so the code block floated inside the frame instead of being it. Body padding removed; the option is now `edgeInset`, meaning only the gap kept between a popup and the frame edge.
- [x] 10.4 Redundant resize traffic removed — the shell no longer re-sends a height it has already reported.

## 11. Docs, CI, decisions

- [x] 11.1 Package README — the split, the artifact layout, CI setup, publishing the integration, authoring, what the shell adds, limits
- [x] 11.2 Root README section pointing at it
- [x] 11.3 Re-scope decision 001 to build-time bridges and correct its WASM rationale; add decision 006
- [x] 11.4 CI: build the package, run its tests, typecheck the integration, validate the manifest; build it in the web-rendering job too
- [x] 11.5 Add the package to the npm publish workflow
- [x] 11.6 ROADMAP entry

## 12. Review findings

- [x] 12.1 `canonicalizeSnippet` dropped `^\n+` only, so a leading line of spaces changed the key despite the documented form; it now drops whole whitespace-only lines
- [x] 12.2 `findFences` split on `\n` and left `\r` behind, so a CRLF document's closing fence never matched and the fence ate the rest of the file; line endings are normalised before scanning
- [x] 12.3 The block defaulted with `||`, so a deliberately empty fence displayed the sample snippet; now `??`
- [x] 12.4 A value flag followed by another option took it as the value (`--out --check` lost check mode); a following `--option` is now a missing value
- [x] 12.5 `action.yml` interpolated inputs straight into bash, so a value containing `$(…)` would execute; every input now reaches the script through `env`
- [x] 12.6 `update-fixtures` sliced on an `indexOf` that could be `-1`, silently committing a one-character fixture; a missing fragment marker now fails the run
- [x] 12.7 The dev server bound `127.0.0.1` but advertised `localhost`, unreachable where `localhost` resolves to `::1` first; it now advertises what it bound
- [x] 12.8 The copy-fidelity spec asserted a line count and a substring, which doubled interior newlines would pass; it now compares against the fixture's exact processed source
- [x] 12.9 Two comments described superseded designs (`HtmlRenderer`'s block-line model, the gallery's Shiki artifacts)

## 13. Deferred (not in this change)

- [ ] 12.1 Live editor preview via Roslyn on `dotnet.wasm`: byte-array reference loading (`CreateFromImage`), a browser build target, ref assemblies as static assets. Blocked on measuring download-to-first-hover; see design.md and decision 006.
- [ ] 12.2 Batch `glosharp render` in the .NET CLI, to resolve references once per run instead of once per snippet. Only worth it if the current cost becomes a bottleneck.
- [ ] 12.3 Public GitBook marketplace listing (needs GitBook review and an owning organization).
- [ ] 12.4 Confirm the research doc's open questions against a live space: webframe sizing on snippet-heavy pages, fence attributes arriving as props, search/AI indexing of block props.
