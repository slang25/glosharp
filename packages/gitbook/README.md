# @glosharp/gitbook

Glo# for [GitBook](https://gitbook.com) — C# snippets with compiler-derived hover
information, XML docs, and diagnostics, in a hosted docs platform that cannot run
Roslyn.

Two halves, one package:

| Half | What it is | Who runs it | Where it lives |
| --- | --- | --- | --- |
| **The integration** (`gitbook-manifest.yaml` + `src/integration/`) | A GitBook ContentKit integration: a custom block bound to the `glosharp` code fence, rendered through a sandboxed webframe. | GitBook, on every page view | The Glo# repository (publish it from a clone, see [Installing the integration](#installing-the-integration)); not in the npm package |
| **The artifact builder** (`glosharp-gitbook` CLI) | Scans your Markdown for `glosharp` fences and publishes one pre-rendered HTML fragment per snippet, keyed by a hash of the snippet. | Your CI | This npm package |

## Why it is split this way

GitBook renders your content on its own infrastructure. There is no remark/rehype
hook, no Shiki transformer, and no raw HTML in content — the only extension point
is an integration, and integrations run in a Workers-style sandbox with no child
processes and no filesystem. Roslyn cannot run there.

So the compile happens where a .NET SDK already exists — your repo's CI — and the
result is published as static HTML. The block in GitBook hashes the fence body it
was given and fetches the matching fragment. Content addressing means the two
sides never have to agree on anything but the code itself.

The consequence to know up front: **a snippet renders as plain code until CI has
published it.** That includes while you are editing it in GitBook.

## The artifact layout

```
<out>/
  index.json                    # what was published (deterministic, diffable)
  github-dark/<sha256>.html     # `glosharp render` output, self-contained
  github-light/<sha256>.html
```

`<sha256>` is the SHA-256 of the fence body after a deliberately minimal
canonicalisation: CRLF → LF, and leading/trailing blank space dropped. Nothing
inside the snippet is touched, because trailing whitespace on an interior line
can matter inside a raw string literal. It is the same key every Glo#
integration uses (`snippetKey` / `canonicalizeSnippet` from `@glosharp/core`).

Point `--out` at a dedicated directory. The builder refuses to write next to an
`index.json` that is not a Glo# index, and `--prune` only ever deletes files
named `<sha256>.html` inside theme directories it knows about (this build's
themes, themes pinned by fences, and those listed in the previous
`index.json`) — anything else in the directory is left alone.

## Preview it locally

You do not need a GitBook account to see what the block will show:

```sh
npx glosharp-gitbook dev docs --framework net10.0
# Glo# preview: http://127.0.0.1:4180/
```

Frames appear in document order, and a fence that pins `theme="…"` is shown
in that theme, as the real block does.

That serves the real webframe shell, renders your fences into a temp directory,
and drives the frames the way GitBook does. Edit your Markdown and reload —
only snippets whose text changed are re-rendered.

In this repo:

```sh
dotnet build src/GloSharp.Cli -c Release
npm run dev:frame -w @glosharp/gitbook
```

| Dev option | |
| --- | --- |
| `--port <n>` | Port (default 4180) |
| `--frame-theme <t>` | `auto` (follow the browser), `github-dark`, `github-light` |
| `--out <dir>` | Artifacts directory (default: a stable per-project temp directory) |
| `--fresh` | Re-render everything on reload, not just new snippets |
| `--no-build` | Serve existing artifacts without rendering |

## CI setup

With the composite action:

```yaml
- uses: actions/checkout@v7
- uses: slang25/glosharp/packages/gitbook@main   # the Glo# repository, at the ref you trust
  with:
    paths: docs
    out: glosharp-artifacts
    complog: docs/docs.glocontext   # or framework:/project:
- uses: actions/upload-pages-artifact@v3
  with:
    path: glosharp-artifacts
```

The action resolves `package-version` (default `latest`) to an exact
`@glosharp/gitbook` version and installs the `GloSharp.Cli` release with the same
version, since the two are released together. Set `glosharp-version` to choose a
different CLI. Then it runs `glosharp-gitbook build`. Inputs mirror the CLI options below (`allow-errors`, `complog-project`,
`check`, `prune`, …).

Or directly (while Glo# is pre-1.0, install the prerelease tags):

```sh
dotnet tool install --global GloSharp.Cli --prerelease
npm install --save-dev @glosharp/gitbook@alpha
npx glosharp-gitbook build docs --out glosharp-artifacts --prune
```

`build` exits non-zero when a snippet has **unexpected compile errors** (errors
not declared with `// @errors:`), listing each as `file:line:col CODE: message`
— the same promise as twoslash: docs that don't compile don't ship. Declare
errors you mean to show with `// @errors: CS0029`, or pass `--allow-errors` to
publish anyway. It also exits non-zero when any render fails, and then writes no
`index.json` and prunes nothing.

On pull requests, `--check` renders everything and fails if any artifact would
change, so a snippet edit cannot merge without its artifact. `--check --prune`
also fails, listing them, when artifacts would be pruned.

A committed [`.glocontext`](../../README.md#portable-compilation-context-glocontext)
is the cheapest way to give CI compilation context: no restore, no SDK-resolved
NuGet, and it is a few hundred kilobytes.

### CLI

```
glosharp-gitbook build <paths...> --out <dir> [options]
glosharp-gitbook dev   <paths...> [--port <n>] [options]
glosharp-gitbook scan  <paths...> [--fence <lang>] [--json]
```

`<paths...>` are Markdown files or directories (walked recursively for
`.md`/`.mdx`).

| Option | |
| --- | --- |
| `--out <dir>` | Where to publish (required for `build`) |
| `--fence <lang>` | Fence language to claim (default `glosharp`) |
| `--theme <name>` | Repeatable; default is `github-dark` + `github-light` |
| `--concurrency <n>` | Concurrent renders (default 4) |
| `--check` | Report drift, write nothing, exit 1 if anything changed (with `--prune`: or would be pruned) |
| `--skip-existing` | Reuse artifacts already on disk (pairs with a cached out dir) |
| `--prune` | Delete `<theme>/<sha256>.html` artifacts no snippet claims (nothing else) |
| `--allow-errors` | Publish snippets with unexpected compile errors instead of exiting 1 |
| `--framework`, `--project`, `--complog`, `--complog-project`, `--config`, `--cache-dir` | Forwarded to the `glosharp` CLI |
| `--executable <path>` | The `glosharp` executable or `GloSharp.Cli.dll` (default: `$GLOSHARP_EXECUTABLE`, then `glosharp` on PATH, `~/.dotnet/tools`, a local tool) |

Rendering-relevant compilation settings normally live in
`glosharp.config.json`, which the `glosharp` CLI discovers on its own.

## Installing the integration

The integration is not on the GitBook marketplace, and its source is not part
of the npm package (GitBook's CLI bundles it from source, with dev-only
dependencies). Publish it privately to your own organization from a clone of
the Glo# repository:

```sh
git clone https://github.com/slang25/glosharp && cd glosharp
npm install
npm run build -w @glosharp/core
cd packages/gitbook
export GLOSHARP_GITBOOK_ORG=<your-gitbook-organization-id>
npx gitbook auth
npm run gitbook:publish
```

Then install it on a space and set **Artifacts URL** to wherever CI published —
for example `https://acme.github.io/docs/glosharp-artifacts`. The host must send
`Access-Control-Allow-Origin` (GitHub Pages does): the webframe fetches the
fragment from the browser, which keeps the integration from becoming an open
proxy for arbitrary URLs.

`npm run gitbook:dev` runs the integration against a real space for local
iteration; `npm run gitbook:check` validates the manifest and bundles the script
(this runs in CI).

## Authoring

````markdown
```glosharp
var greeting = "Hello, Glo#";
//  ^?
```
````

All the usual Glo# markers work — `^?`, `^|`, `// @errors:`, `// @highlight`, and
the rest — because the fragment is ordinary `glosharp render` output.

Supported fence attributes:

| Attribute | Effect |
| --- | --- |
| `theme="github-light"` | Pin one theme instead of following the reader's colour scheme (the builder renders that theme for the snippet even if it is not in `--theme`) |
| `framework="net10.0"` | Target framework for this snippet, used by the builder |

Fences inside list items and blockquotes are found too; the body is taken
with the item's indentation (or the `>` markers) removed.

`framework` is **not** part of the artifact key, so two byte-identical snippets
asking for different frameworks is an error the builder reports rather than a
silent mis-render.

Fences in other languages, including plain `csharp`, are left alone. Claiming
`csharp` itself would take over every C# fence in every installed space, wanted
or not.

## What the webframe adds

`glosharp render` output is a self-contained fragment with a hard no-JavaScript
guarantee — all hover behaviour is CSS. Inside an iframe that is nearly enough,
but two things cannot be done from inside the fragment:

- **Height.** The frame has no intrinsic size, so the shell measures the fragment
  and reports a height to GitBook.
- **Popups.** A frame cannot paint outside its own box. The shell flips popups to
  open *downward* and asks GitBook for extra height while one is open — growing
  downward keeps the token still, where growing upward would move it out from
  under the pointer. The height it asks for is measured from the *token*, not
  from the popup: a popup taller than the space below its token gets slid upward
  by the browser to stay in view, so measuring the popup would report that it
  already fits while it sits on top of the code.
- **Being answered at all.** The shell re-announces itself until GitBook sends it
  state. A single announcement is a race lost to any host that attaches its
  listener late, and losing it leaves the frame blank forever.

- **Trust.** The shell only accepts state from the page that embeds it
  (`event.source === window.parent`), only accepts plain theme names, parses
  each fetched fragment with `DOMParser` and drops scripts, frames, event
  handlers and non-https URLs before inserting it, and carries a
  Content-Security-Policy that lets only its own script run (by hash) and only
  fetch from https hosts. A lookup that misses names the artifact it looked
  for (theme and key prefix) in the frame and in the console, so key drift
  between CI and GitBook can be debugged.

These are exercised in the browser by `tests/rendering`
(`specs/gitbook-frame.spec.ts`), which drives the real shell through GitBook's
message contract over real `glosharp render` artifacts.

## Limits

- Snippets are stale until CI runs, including in the editor. Live preview would
  need Roslyn in the browser (see decision 006).
- One iframe per snippet. Each shell is tiny and cacheable, but N snippets is N
  documents.
- Each theme is a separate `glosharp render` (a full compile), plus one
  `glosharp process` per snippet for the compile-error check (skip it with
  `--allow-errors`). The CLI's `render` takes one theme and reports no compile
  status, so the work can't be shared yet; `--skip-existing` and `--cache-dir`
  keep repeat builds cheap.
- GitBook's search and AI index the fence body (it lives in the content model as
  a block prop); the rendered hover text is not indexed.
