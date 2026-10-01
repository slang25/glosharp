# @glosharp/core

Node.js bridge for **Glo#** ("twoslash for C#"): runs the `glosharp` CLI on a C#
snippet and returns the compiler's view of it — hover signatures and XML docs,
diagnostics, completions, highlights and custom tags — as typed JSON.

Most sites don't use this package directly: [`@glosharp/shiki`](../shiki)
(Shiki, Astro, Docusaurus, plain remark) and `@glosharp/expressive-code` build
on it. Use it when you render the results yourself.

ESM only. Node 18.17+.

## Install

The bridge drives the .NET CLI, so you need both. While Glo# is pre-1.0 every
release is a prerelease:

```sh
dotnet tool install --global GloSharp.Cli --prerelease
# or, per repository:
dotnet new tool-manifest && dotnet tool install GloSharp.Cli --prerelease

npm install @glosharp/core@alpha
```

The CLI needs a .NET SDK on the machine that builds your docs (see the Glo#
repository for supported versions).

## Usage

```ts
import { createGloSharp } from '@glosharp/core'

const glosharp = createGloSharp()

const result = await glosharp.process({
  code: `var greeting = "Hello";
//  ^?`,
})

result.code            // the snippet with markers removed
result.hovers[0].text  // "(local variable) string greeting"
result.meta.compileSucceeded
```

`process()` returns a `GloSharpResult`; `render()` returns the CLI's own
self-contained HTML fragment for the same input (`theme`, `standalone`).

```ts
const html = await glosharp.render({ code, theme: 'github-light' })
```

Inputs are either `code` (sent on stdin) or `file` (a path the CLI reads).
`region` works with both.

## Options

`createGloSharp(options)` — instance-wide:

| Option | Default | |
| --- | --- | --- |
| `executable` | auto | The CLI to run: a path or command, a `GloSharp.Cli.dll` (run with `dotnet`), or an argv prefix such as `['dotnet', 'path/GloSharp.Cli.dll']`. |
| `framework` | CLI default (`net8.0`) | Target framework, e.g. `net10.0`. |
| `cacheDir` | none | Directory for the CLI's on-disk result cache. |
| `configFile` | discovered | Path to `glosharp.config.json` (otherwise discovered from the working directory). |
| `complog` | none | `.complog` / `.glocontext` compilation context. |
| `complogProject` | none | Project to use from a multi-project complog. |
| `concurrency` | none | Extra per-instance cap on concurrent CLI processes (see below). |
| `timeoutMs` | `$GLOSHARP_TIMEOUT_MS` or `180000` | Kill a CLI run after this long; `0` disables. |
| `cacheSize` | `1000` | Results kept in the in-memory cache (LRU). |

`process(options)` / `render(options)` — per call: `code` or `file`,
`framework`, `project` (a `.csproj` for NuGet references), `region`,
`noRestore`, `cacheDir`, `configFile`, `complog`, `complogProject`,
`timeoutMs`, `signal` (an `AbortSignal`); `render` also takes `theme` and
`standalone`.

### Finding the CLI

Without `executable`, the bridge tries, in order: `$GLOSHARP_EXECUTABLE`,
`glosharp` on `PATH` (with `PATHEXT` on Windows), `~/.dotnet/tools/glosharp`,
and a local tool (`dotnet glosharp`, when `dotnet tool list --local` lists
`GloSharp.Cli`). The answer is cached for the life of the process.

To use a build from source:

```sh
dotnet build src/GloSharp.Cli -c Release
GLOSHARP_EXECUTABLE=$PWD/src/GloSharp.Cli/bin/Release/net8.0/GloSharp.Cli npm run build
```

### Concurrency, timeouts and caching

- Every CLI run is a full Roslyn compilation (about 100 MB). All instances in
  a process share one limit: `$GLOSHARP_CONCURRENCY`, or
  `max(1, min(cpus - 1, 8))`. Change it with
  `configureGloSharp({ concurrency: 4 })`.
- A run that exceeds `timeoutMs` is killed (the whole process tree on
  Windows) and rejects with a `timeout` error. Running CLIs are killed when
  Node exits.
- Results are cached in memory per instance, keyed by the code (or the file's
  path, size and modification time), every option that reaches the CLI, and
  the working directory. Identical concurrent calls share one run; failures
  are not cached.

## Errors

Compile errors in a snippet are **not** exceptions: they are in
`result.errors` (visible code) and `result.hiddenErrors` (code hidden with
`---cut---` markers), and `result.meta.compileSucceeded` is `false` when any of
them was not declared with `// @errors:`. `unexpectedErrors(result)` returns
exactly those.

When the CLI itself fails, the bridge throws a `GloSharpCliError`:

| `kind` | When |
| --- | --- |
| `not-found` | No CLI could be found (the message says how to install it). |
| `spawn` | The CLI could not be started. |
| `exit` | The CLI exited non-zero; `exitCode` and `stderr` are set. |
| `timeout` | It ran longer than `timeoutMs`. |
| `aborted` | Your `signal` fired. |
| `invalid-output` | It succeeded but printed something that isn't JSON. |

Every error message names the snippet (its first line).

## Result shape

`GloSharpResult` (see `src/types.ts`; the CLI's JSON is documented in
`design/data-format.md`):

| Field | |
| --- | --- |
| `code` | The snippet with markers and cut code removed — what to display. |
| `original` | The input as given. |
| `hovers` | Hover targets: position, `text`, structured `parts`, `docs`, `persistent` (`^?` queries), `overloadCount`, … |
| `errors` | Diagnostics in visible code: position (`line`/`character`, optional `endLine`/`endCharacter`), `sourceLine`/`sourceCharacter` (position in the original input, for `file:line` reporting), `code` (`CS…`, or `GS…` for Glo#'s own), `message`, `severity`, `expected`. |
| `hiddenErrors` | Diagnostics in hidden code (same shape). |
| `completions` | `^|` completion lists. |
| `highlights` | `@highlight`, `@focus`, `@diff` lines. |
| `tags` | `@log`, `@warn`, `@error`, `@annotate`. |
| `meta` | `targetFramework`, `packages`, `compileSucceeded`, `warnings` (non-fatal problems such as a failed package restore), … |

## Helpers for integrations

- `snippetKey(code, options?)` — a stable key for a snippet plus the options
  that change its output (`framework`, `project`, `region`, `noRestore`,
  `configFile`, `complog`, `complogProject`). Line endings and leading/trailing
  blank lines are normalised, so a pipeline that strips the final newline still
  finds the result. Use it on both sides of any lookup.
- `canonicalizeSnippet(code)` and `hasGloSharpMarkers(code)` — also available
  from `@glosharp/core/snippet`, which has no Node.js dependencies.
- `resolveExecutable()` / `clearExecutableCache()` — the discovery above.

## Markers

A quick reference (see `@glosharp/shiki`'s README for more):

| Marker | Effect |
| --- | --- |
| `//  ^?` | Show the type of the token above the caret (always visible). |
| `//  ^|` | Show completions at the caret. |
| `// @errors: CS0029 CS0103` | The next code line is expected to have these errors (comma- or space-separated); they are shown, and the snippet still counts as compiling. |
| `// @noErrors` | Don't report or fail on any errors. |
| `// ---cut---` / `---cut-after---` / `---cut-start---` … `---cut-end---` | Hide setup code (still compiled). |
| `// @highlight`, `// @focus`, `// @diff: +` / `-` | Line annotations for the next line. |
| `// @log:`, `// @warn:`, `// @error:`, `// @annotate:` | A callout after the previous line. |
| `#:package Name@1.2.3` | Reference a NuGet package (file-based-app syntax). |
