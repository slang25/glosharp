# Glo# Examples

Four small sites and scripts showing Glo# in different contexts. Each one is an npm workspace of
this repository and builds against the local packages and a locally built CLI.

| Example | Integration | What to look at |
| --- | --- | --- |
| [`expressive-code/`](./expressive-code/) | `@glosharp/expressive-code` in Astro | `astro.config.mjs`: one plugin, every C# block gets hovers |
| [`astro-blog/`](./astro-blog/) | `@glosharp/shiki` in Astro Markdown | `src/remark-glosharp.mjs` + `astro.config.mjs` |
| [`docusaurus-docs/`](./docusaurus-docs/) | `@glosharp/shiki` in Docusaurus | `src/plugins/glosharp-plugin.ts` |
| [`standalone/`](./standalone/) | `@glosharp/shiki` in a Node script | `render.mjs`: C# files in, one HTML page out |

## Running an example from this repository

From the repository root:

```bash
# 1. Install dependencies and build the @glosharp/* packages
npm ci
npm run build

# 2. Build the CLI and tell the examples where it is
npm run cli:build
export GLOSHARP_EXECUTABLE="$PWD/src/GloSharp.Cli/bin/Release/net8.0/GloSharp.Cli"
# Windows: $env:GLOSHARP_EXECUTABLE = "$PWD\src\GloSharp.Cli\bin\Release\net8.0\GloSharp.Cli.exe"

# 3. Run one
npm run dev -w examples/expressive-code      # or: npm run build -w examples/expressive-code
npm run dev -w examples/astro-blog
npm start   -w examples/docusaurus-docs
npm run render -w examples/standalone        # then open examples/standalone/output.html
```

If `glosharp` is already on your `PATH`, skip the `GLOSHARP_EXECUTABLE` export.

<!-- TODO(merge): if the CLI's target framework changes, update the net8.0 path segment above. -->

## Using an example outside this repository

The examples depend on `"@glosharp/*": "*"`, which resolves to the local workspace here. In your
own project, install the CLI and the published packages instead, and keep their versions in step:

```bash
dotnet tool install --global GloSharp.Cli --prerelease
npm install @glosharp/expressive-code          # or @glosharp/shiki
```

## Notes per example

**`expressive-code/`** is the simplest integration: add `pluginGloSharp()` to Expressive Code and
all C# blocks get hovers, errors and completions, with no stylesheet to add.

**`astro-blog/`** uses Astro's built-in Shiki. Shiki transforms synchronously but glosharp has to
run the compiler, so a remark plugin runs `processGloSharpBlocks` over each page's C# blocks
first, and `transformerGloSharpFromMap` applies the results while Shiki highlights. The layout
imports `@glosharp/shiki/style.css`, which positions the popups. On Astro 7, `remarkPlugins` need
the `@astrojs/markdown-remark` package installed, which is why it's a dependency.

**`docusaurus-docs/`** uses the same two steps inside a remark plugin, and replaces each C# block
with the rendered HTML. `src/css/custom.css` imports `@glosharp/shiki/style.css` and switches the
code to the dark Shiki theme in Docusaurus's dark mode.

**`standalone/`** reads the `.cs` files next to it and writes `output.html`, inlining
`@glosharp/shiki/style.css`. If you don't need Shiki, the CLI does this by itself:
`glosharp render snippet.cs --standalone --output snippet.html`.

## Marker syntax

The examples use a few markers; see the [marker reference](../README.md#marker-reference) for all
of them.

| Marker | Purpose |
| --- | --- |
| `// ^?` | On its own line: pin the hover for the token above the caret |
| `// ^\|` | On its own line: show completions at the caret position above |
| `// @errors: CS0103` | The next line has this error on purpose; `glosharp verify` fails if it doesn't |
| `// @noErrors` | Suppress all errors (twoslash semantics). It turns checking off, so avoid it on snippets you want verified |
| `// ---cut---` | Hide the code above (it's still compiled) |
