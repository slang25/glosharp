# Glo#

**twoslash for C#**: compile the C# snippets in your docs with Roslyn, and render them with
IDE-quality hover tooltips, completions and compiler errors. Snippets that stop compiling fail
your CI build instead of reaching readers.

[`@glosharp/expressive-code`](packages/expressive-code/README.md) ·
[`@glosharp/shiki`](packages/shiki/README.md) ·
[`@glosharp/core`](packages/glosharp/README.md) ·
[`@glosharp/gitbook`](packages/gitbook/README.md) ·
[Examples](examples/README.md)

> **Status: preview.** Only prerelease packages are published, so install with
> `--prerelease` (NuGet). Expect breaking changes between prereleases, and keep the CLI and the
> npm packages on matching versions.

## How it works

```
your-snippet.cs ──► glosharp CLI (Roslyn) ──► JSON: hovers, errors, completions
                                                │
             Expressive Code / Shiki / GitBook ◄┘  or  `glosharp render` → HTML
```

The `glosharp` .NET tool compiles each snippet and reports what the compiler knows about every
token. The npm packages call the CLI at build time and turn its output into CSS-positioned
popups, so your readers download no JavaScript for the tooltips.

## Quickstart

You need the [.NET SDK](https://dotnet.microsoft.com/download) (8 or later; 10 or later for
`#:package` snippets).

**1. Install the CLI**

```sh
dotnet tool install --global GloSharp.Cli --prerelease
glosharp --version
```

**2. Write a snippet.** Markers are ordinary comments, so the file is still valid C#:

```csharp
// hello.cs
var greeting = "Hello, World!";
var shout = greeting.ToUpper();
//                   ^?
Console.WriteLine(shout);
```

`// ^?` on its own line pins the hover for the token above the caret. Every other token gets a
hover too; `^?` just keeps that one open.

**3. Look at the result**

```sh
glosharp render hello.cs --standalone --output hello.html   # open hello.html in a browser
glosharp process hello.cs                                    # the JSON integrations consume
```

**4. Check snippets in CI**

```sh
glosharp verify docs/snippets            # files and/or directories; exit code 1 on failure
```

Failures are printed as `path(line,col): error CODE: message`, the same shape as compiler output.

**5. Pick an integration**

| You use | Package | Start here |
| --- | --- | --- |
| Astro, Starlight or anything on Expressive Code | `@glosharp/expressive-code` | [README](packages/expressive-code/README.md), [example](examples/expressive-code/) |
| Shiki (Astro Markdown, VitePress, Docusaurus, Node scripts) | `@glosharp/shiki` | [README](packages/shiki/README.md), [Astro example](examples/astro-blog/), [Docusaurus example](examples/docusaurus-docs/), [Node script](examples/standalone/) |
| GitBook | `@glosharp/gitbook` | [README](packages/gitbook/README.md) |
| Your own pipeline | `@glosharp/core` | [README](packages/glosharp/README.md) |
| Hugo, Jekyll, plain HTML | the CLI | `glosharp render --standalone` |

For example, with Expressive Code (`npm install @glosharp/expressive-code`):

```js
// ec.config.mjs
import { defineEcConfig } from 'astro-expressive-code'
import { pluginGloSharp } from '@glosharp/expressive-code'

export default defineEcConfig({
  plugins: [pluginGloSharp()],
})
```

Every `csharp` block now gets hovers.

## Marker reference

Marker lines are removed from the rendered code. Lines are written below for a snippet as the
author sees it.

### Queries

| Marker | Meaning |
| --- | --- |
| `// ^?` | On its own line: pin the hover of the token above the caret. |
| `// ^\|` | On its own line: show the completion list at the caret position on the line above. |

The caret's column is a character index into the line above: a tab counts as one character, so
align carets with spaces. A query targets the nearest code line above it.

### Errors

| Marker | Meaning |
| --- | --- |
| `// @errors: CS0029, CS1503` | These errors are expected **on the next code line**. Codes are separated by commas and/or spaces. The errors are still shown, and `verify` fails if one of them doesn't occur. |
| `// @suppressErrors` | Hide all diagnostics for the snippet. |
| `// @suppressErrors: CS0168, CS0219` | Hide only the listed diagnostics. |
| `// @noErrors` | Same as `@suppressErrors` (the twoslash name). It **turns checking off** for the snippet, so don't use it on snippets you want `verify` to check. |

`glosharp verify` fails on any error that isn't expected or suppressed, including errors in
hidden (cut) code, and on an `@errors` expectation that doesn't occur (reported as `GS0003`).
Warnings are shown but never fail verification. Glo#'s own diagnostics use `GS` codes; see
[design/data-format.md](design/data-format.md).

### Annotations

| Marker | Meaning |
| --- | --- |
| `// @highlight` | Highlight the **next** line. |
| `// @highlight: 2-4` | Highlight lines 2 to 4 of the **rendered** output (1-based, counted after marker and cut lines are removed), wherever the marker is. `@highlight: 3` targets one line. |
| `// @focus`, `// @focus: 2-4` | Like `@highlight`, but dims every other line. |
| `// @diff: +`, `// @diff: -` | Mark the **next** line as added or removed. |
| `// @log: text` | Attach a callout to the line **above**. Also `@warn:`, `@error:` and `@annotate:`. |

Note the directions: `@highlight`, `@focus` and `@diff` apply to the line after them, callouts to
the line before them.

### Hiding code

Hidden code is still compiled, so it can declare what the visible part needs.

| Marker | Meaning |
| --- | --- |
| `// ---cut---` (or `// ---cut-before---`) | Hide everything above, including this line. |
| `// ---cut-after---` | Hide everything below, including this line. |
| `// ---cut-start---` … `// ---cut-end---` | Hide the lines in between. An unclosed `cut-start` hides to the end. |

C# requires top-level statements before type declarations, so put helper types *below*
`// ---cut-after---` rather than above `// ---cut---`.

To show part of a larger file, wrap it in `#region Name` / `#endregion` and pass
`--region Name` (or the `region` option in the npm packages). The whole file is compiled.

### Compiler settings

| Marker | Values | Default |
| --- | --- | --- |
| `// @nullable: disable` | `enable`, `disable`, `warnings`, `annotations` | `enable` |
| `// @langVersion: 12` | `7`, `7.1`, `7.2`, `7.3`, `8` … `13`, `latest`, `preview`, `default` | `latest` |
| `#:package Name@Version` | Reference a NuGet package (.NET 10+ SDK) | |
| `#:sdk Microsoft.NET.Sdk.Web` | Compile as that SDK, e.g. for ASP.NET Core types | `Microsoft.NET.Sdk` |
| `#:property Name=Value` | Set an MSBuild property | |

Snippets get the SDK's implicit usings (`System`, `System.Collections.Generic`, `System.IO`,
`System.Linq`, `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`; plus the ASP.NET
Core ones for `#:sdk Microsoft.NET.Sdk.Web`). An invalid `@langVersion` or `@nullable` value is
reported as `GS0001` or `GS0002`.

<!-- TODO(merge): if C# 14 lands in CompilationOptionsMapper, add `14` to the @langVersion values. -->

## Compiling against your own code and packages

A snippet compiles against the .NET base class library by default (choose the version with
`--framework net10.0`). To get more:

| You want | Use |
| --- | --- |
| NuGet packages in one snippet | `#:package` directives (.NET 10+ SDK) |
| The packages of an existing project | `--project path/to/Docs.csproj` (restored automatically; `--no-restore` to skip) |
| Your library's types, or a build that doesn't need the SDK and NuGet cache | a compilation log: `--complog docs.glocontext` |

### Creating a compilation log

A compilation log records the references a project compiled against: the framework, its NuGet
packages and the projects it references. Create it from a project that *references* the code you
document (for example a docs or samples project), then compact it into a small `.glocontext` you
can commit:

```sh
# Option A: with the complog tool (note: the tool is "complog", not "Basic.CompilerLog")
dotnet tool install --global complog
complog create samples/Samples.csproj -o docs.complog
glosharp compact-complog docs.complog -o docs.glocontext

# Option B: straight from an MSBuild binary log
dotnet build samples/Samples.csproj -bl:docs.binlog --no-incremental
glosharp compact-complog docs.binlog -o docs.glocontext

glosharp verify docs/snippets --complog docs.glocontext
```

Build with `--no-incremental` for Option B: an up-to-date build skips the compiler and records no
compilations. Use `--complog-project Name` when the log contains several projects.

### What's in a `.glocontext`

`glosharp compact-complog` keeps just what Glo# needs to resolve types, hovers and completions:

- Framework reference assemblies become *pointers* into their NuGet targeting packs
  (`microsoft.netcore.app.ref`, …), with a content hash per pack. A compilation that references a
  pack's whole ref set stores a single `packAll` entry.
- The remaining (NuGet and library) references are rewritten with
  [JetBrains.Refasmer](https://github.com/JetBrains/Refasmer) to public API metadata only.
- Analyzers, original sources and generated sources are dropped.
- Identical references are stored once by SHA-256, in a `GLOCTX`-header, zstd-compressed tar with
  a deterministic `manifest.json`.

A ~6 MB BCL-only complog shrinks to under 1 KB, and a ~15 MB ASP.NET + EF complog to about
450 KB. Output is byte-deterministic, so a committed `.glocontext` only changes when the
compilation context does. `--complog` accepts either format.

When a `.glocontext` is opened, pointed-to packs are found in the NuGet global packages folder
(`NUGET_PACKAGES` or `~/.nuget/packages`), then in a glosharp cache (`GLOSHARP_CACHE_DIR` or the
platform local-app-data folder), then in the installed .NET SDK's own `packs/` folder, and
otherwise downloaded once from nuget.org (about 7 MB per pack) and cached. Each pack is verified
against its recorded hash before use, and a local copy that doesn't match is skipped. The
compactor records the nuget.org bytes; official Microsoft SDK builds checked so far ship identical
packs, so no network is needed on a machine with a matching SDK. A differing copy (for example
from a source-built distro SDK) falls through to the download. Cache `GLOSHARP_CACHE_DIR` in CI to
avoid repeated downloads.

For artifacts that must resolve fully offline, pass `--self-contained`: every reference is
embedded (format v1, typically 1–3 MB). If packs can't be acquired at compact time, the compactor
warns and embeds those references.

> The zstd codec is [`ZstdSharp.Port`](https://www.nuget.org/packages/ZstdSharp.Port/) today; it
> will move to .NET 11's built-in zstd with no format change. The header reserves baseline id and
> version slots for a possible future patch-based format. Readers **must** reject files with
> non-zero baseline fields.

## Configuration

`glosharp init` writes a `glosharp.config.json`. The CLI finds it by walking up from the current
directory (or use `--config path`), and command-line flags override it. Relative paths resolve
against the config file.

```jsonc
{
  "framework": "net10.0",
  "project": "./samples/Samples.csproj",   // or "complog": "./docs.glocontext"
  "cacheDir": ".glosharp-cache",           // reuse results across runs
  "render": { "theme": "github-dark", "standalone": false }
}
```

Run `glosharp --help` or `glosharp <command> --help` for every option.

## CI

```yaml
# .github/workflows/docs.yml
jobs:
  snippets:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7
      - uses: actions/setup-dotnet@v5
        with:
          dotnet-version: 10.0.x
      - run: dotnet tool install --global GloSharp.Cli --prerelease
      - run: glosharp verify docs/snippets --cache-dir .glosharp-cache
```

Pin the tool with `--version` once you depend on it. If your docs site renders snippets with an
npm integration, run `verify` as well: integrations render errors, they don't fail the build.

## GitBook

GitBook has no Markdown pipeline to hook, so [`@glosharp/gitbook`](packages/gitbook/README.md)
ships a GitBook integration that claims the `glosharp` code fence and renders it in a sandboxed
webframe, plus a `glosharp-gitbook` CLI that your CI runs to pre-render every fence into
content-addressed HTML:

```sh
npx glosharp-gitbook build docs --out glosharp-artifacts --prune   # publish from CI
npx glosharp-gitbook dev   docs                                    # preview locally, no account needed
```

The trade-off is staleness: a snippet renders as plain code until CI publishes it. See
[decision 006](design/decisions.md) and [research/07](research/07-gitbook-integration.md).

## Contributing

See [CONTRIBUTING.md](CONTRIBUTING.md) for building, testing (including the browser rendering
suite) and the OpenSpec workflow.

## Project docs

- Design: [architecture](design/architecture.md), [JSON output format](design/data-format.md),
  [integration points](design/integration-points.md), [decisions](design/decisions.md)
- Research notes: [twoslash](research/01-twoslash-architecture.md),
  [Roslyn metadata](research/02-roslyn-metadata-extraction.md),
  [Shiki and Expressive Code](research/03-shiki-and-expressive-code.md),
  [NuGet resolution](research/04-nuget-resolution.md),
  [snippet management](research/05-code-snippet-management.md),
  [doc frameworks](research/06-doc-framework-landscape.md),
  [GitBook](research/07-gitbook-integration.md)
- [Roadmap](ROADMAP.md) · [Annotated links](references/links.md)

`openspec/` (specs and change proposals) and `.claude/` are maintainer tooling.

## License

[MIT](LICENSE)
