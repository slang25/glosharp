# @glosharp/shiki

**Glo#** ("twoslash for C#") for [Shiki](https://shiki.style): C# code blocks
with compiler-accurate hover popups, XML docs, errors, completions,
highlights and callouts — rendered at build time, no client JavaScript.

Includes ready-made Markdown integrations, so you don't write glue code:

| You use | Add |
| --- | --- |
| Astro 7.3+ (default Sätteri processor) | `satteriGloSharp()` + `transformerGloSharp()` |
| Astro with `unified()`, `@shikijs/rehype`, any remark → Shiki pipeline | `remarkGloSharp()` + `transformerGloSharp()` |
| Docusaurus, MDX, plain remark → rehype | `remarkGloSharp()` (renders with Shiki itself) |
| Your own code | `processGloSharpBlocks()` + `transformerGloSharpFromMap()` |

ESM only. Node 18.17+. Shiki 3 or 4.

## Install

```sh
# the compiler-side CLI (pre-1.0 releases are prereleases)
dotnet tool install --global GloSharp.Cli --prerelease
# or per repository: dotnet new tool-manifest && dotnet tool install GloSharp.Cli --prerelease

npm install @glosharp/shiki@alpha shiki
```

Then include the stylesheet once:

```js
import '@glosharp/shiki/style.css'
```

```css
/* or from CSS */
@import '@glosharp/shiki/style.css';
```

The CLI is found automatically (`$GLOSHARP_EXECUTABLE`, `glosharp` on PATH,
`~/.dotnet/tools`, or a local tool); set the `executable` option or
`GLOSHARP_EXECUTABLE` to use a specific build. See
[`@glosharp/core`](../glosharp) for discovery, concurrency and timeouts.

## Which blocks are processed

A fenced block is a Glo# block when its language is `csharp`, `cs` or `c#`
(configurable with `languages`) and:

- it contains Glo# markers (`^?`, `^|`, `// @errors:`, `// ---cut---`,
  `// @highlight`, `#:package`, …), **or**
- its fence meta contains `glosharp` — use this to get hover information on a
  plain block:

  ````md
  ```csharp glosharp
  var greeting = "Hello";
  ```
  ````

Blocks without markers or `glosharp` are left exactly as they are, so adding
Glo# to an existing site doesn't turn illustrative fragments red. Opt a block
out with `no-glosharp` in its meta. A ```` ```glosharp ```` fence (as used by
the GitBook integration) is always a Glo# C# block.

- `explicitTrigger: true` processes only blocks whose meta contains `glosharp`
  (a RegExp is tested against the meta instead), like twoslash.
- `processUnmarked: true` processes every C# block.

Per-block options go in the meta: `region=Name` (extract a `#region`) and
`framework=net10.0`.

## Framework recipes

### Astro 7.3+ (Sätteri, the default)

```js
// astro.config.mjs
import { defineConfig } from 'astro/config'
import { satteri } from '@astrojs/markdown-satteri'
import { satteriGloSharp, transformerGloSharp } from '@glosharp/shiki'

export default defineConfig({
  markdown: {
    processor: satteri({ mdastPlugins: [satteriGloSharp()] }),
    shikiConfig: {
      themes: { light: 'github-light', dark: 'github-dark' },
      transformers: [transformerGloSharp()],
    },
  },
})
```

```astro
---
// in your layout
import '@glosharp/shiki/style.css'
---
```

`satteriGloSharp` compiles the blocks; Astro's own Shiki pass renders them with
your themes and other transformers, and `transformerGloSharp` adds the Glo#
markup. `npm install @astrojs/markdown-satteri` if it isn't already a
dependency.

### Astro with the unified (remark) processor

```js
import { unified } from '@astrojs/markdown-remark'
import { remarkGloSharp, transformerGloSharp } from '@glosharp/shiki'

export default defineConfig({
  markdown: {
    processor: unified({ remarkPlugins: [[remarkGloSharp, { render: 'transformer' }]] }),
    shikiConfig: { transformers: [transformerGloSharp()] },
  },
})
```

The same pairing works for any pipeline where Shiki runs after remark
(`@shikijs/rehype`, `rehype-pretty-code`): `render: 'transformer'` leaves the
code block in place and tags it (`glosharp-key=…` in the meta) for
`transformerGloSharp`.

### Docusaurus

```ts
// docusaurus.config.ts
import { remarkGloSharp } from '@glosharp/shiki'

presets: [
  ['classic', {
    docs: {
      beforeDefaultRemarkPlugins: [[remarkGloSharp, { /* options */ }]],
    },
    theme: { customCss: './src/css/custom.css' },
  }],
],
```

```css
/* src/css/custom.css */
@import '@glosharp/shiki/style.css';
```

Docusaurus highlights with Prism, so `remarkGloSharp` renders Glo# blocks with
Shiki itself (`render: 'html'`, the default when no `transformerGloSharp` is in
play) and inserts them as a `<div class="glosharp-block">` — MDX would
otherwise re-render the `<pre>`. Dark mode follows Docusaurus's
`[data-theme='dark']` automatically. Glo# blocks don't get Docusaurus's own
code-block chrome (copy button, title bar); other blocks are untouched.

### Plain Node (remark → rehype)

```js
import { unified } from 'unified'
import remarkParse from 'remark-parse'
import remarkRehype from 'remark-rehype'
import rehypeStringify from 'rehype-stringify'
import { remarkGloSharp } from '@glosharp/shiki'

const file = await unified()
  .use(remarkParse)
  .use(remarkGloSharp, { shiki: { themes: { light: 'github-light', dark: 'github-dark' } } })
  .use(remarkRehype)
  .use(rehypeStringify)
  .process({ value: markdown, path: 'docs/intro.md' })
```

### Your own Shiki calls

```js
import { codeToHtml } from 'shiki'
import { processGloSharpBlocks, transformerGloSharpFromMap } from '@glosharp/shiki'

const results = await processGloSharpBlocks(blocks, { project: './Docs.csproj' })
const html = await codeToHtml(code, {
  lang: 'csharp',
  themes: { light: 'github-light', dark: 'github-dark' },
  transformers: [transformerGloSharpFromMap(results)],
})
```

Shiki's transformer hooks are synchronous, so compiling happens first, in a
batch. For a single snippet: `processGloSharpCode(code, options)` then
`transformerGloSharpWithResult(result)`.

## Options

### Markdown integrations (`remarkGloSharp`, `satteriGloSharp`)

| Option | Default | |
| --- | --- | --- |
| `languages` | `['csharp', 'cs', 'c#']` | Fence languages treated as C#. |
| `explicitTrigger` | `false` | Only process blocks opted in with `glosharp` meta (or matching a RegExp). |
| `processUnmarked` | `false` | Process every C# block, markers or not. |
| `onCompileError` | `'warn'` | Unexpected compile errors: `'warn'` logs `file:line:col CODE: message`, `'throw'` fails the build, `'ignore'`. The errors are rendered either way. |
| `onCliError` | `'throw'` | CLI missing, crashed or timed out: `'throw'` fails the build with the file, line and install instructions; `'warn'` logs once and renders the block as plain code. |
| `render` | `'auto'` | `remarkGloSharp` only. `'transformer'`: leave blocks for the site's Shiki pass (needs `transformerGloSharp()`). `'html'`: highlight here and replace the block. `'auto'`: `'transformer'` if `transformerGloSharp()` has been created in the process, else `'html'`. |
| `shiki` | dual github-light/dark | `render: 'html'` only: `{ theme }` or `{ themes, defaultColor }`, plus extra `transformers`. |
| `output` | `'auto'` | `render: 'html'` only: `'mdx'` (a JSX element) when the processor compiles to JavaScript, else `'hast'`. |
| `logger` | `console` | Where warnings go (`{ warn(message) }`). |
| `project`, `region`, `framework`, `noRestore` | | Passed to the CLI for every block. |
| `executable`, `cacheDir`, `configFile`, `complog`, `complogProject`, `concurrency`, `timeoutMs` | | Bridge options (see `@glosharp/core`). |
| `focusable`, `completionLimit` | | Rendering options (below). |

A good CI setting is `onCompileError: 'throw'`: docs whose snippets stop
compiling fail the build, the way twoslash does.

### Rendering (every transformer and integration)

| Option | Default | |
| --- | --- | --- |
| `focusable` | `true` | Hover targets get `tabindex="0"`, so keyboard users can reach them; the popup opens on focus. |
| `completionLimit` | `12` | Items shown per `^|` list (filtered to the typed prefix, de-duplicated; "… N more" after that). |

### `processGloSharpBlocks(blocks, options)`

`blocks` are strings or `{ code, project?, region?, framework?, noRestore?, force? }`.
Blocks without markers are skipped unless `processUnmarked: true` or
`force: true`. Results are keyed by `snippetKey(code, options)`; the map
remembers the shared options, so `transformerGloSharpFromMap(map)` finds them.
If blocks carry their own options, give the transformer the same ones with
`transformerGloSharpFromMap(map, { blockOptions: (code, meta) => ({ project }) })`.
`onCliError: 'warn'` drops failing blocks instead of rejecting (the rejection
names the block).

## What gets rendered

- **Hovers** — signature with syntax colours, overload count, XML docs
  (summary, parameters, returns, exceptions, remarks). Popups open below the
  token (above or to the other side when there's no room) using CSS anchor
  positioning, with a fallback for browsers without it.
- **`^?` queries** — always visible below the line.
- **Errors** — a squiggle under the range and the message on its own row;
  `CS` codes link to the docs. Errors declared with `// @errors:` are shown too
  (they're how you show an error on purpose), marked `glosharp-error-expected`.
- **Completions** (`^|`), **highlight / focus / diff** lines, and
  **`@log` / `@warn` / `@error` / `@annotate`** callouts.

Output is deterministic (ids and anchor names are derived from the snippet),
so rebuilds produce identical HTML.

## Theming and dark mode

Popups, query boxes and messages take their colours from the code block's
Shiki theme, so they match light and dark themes without configuration, and
popup syntax colours switch between light and dark palettes.

With **dual themes** (`themes: { light, dark }`), Glo# works with Shiki's
usual dark-mode CSS — every element it adds is a `<span>` that declares its own
`--shiki-light` / `--shiki-dark`:

```css
@media (prefers-color-scheme: dark) {
  .shiki, .shiki span {
    color: var(--shiki-dark) !important;
    background-color: var(--shiki-dark-bg) !important;
  }
}
```

(Astro emits `.astro-code` instead of `.shiki`.) Sites that switch with
`html.dark` or `html[data-theme="dark"]` (Docusaurus, Starlight, VitePress,
Tailwind) need nothing: `style.css` switches Glo# blocks itself.

To restyle, override the custom properties on `pre.glosharp`
(`--glosharp-error`, `--glosharp-warning`, `--glosharp-info`,
`--glosharp-accent`, and their `-light` / `-dark` variants) or target the
`glosharp-*` classes.

## Markers

| Marker | Effect |
| --- | --- |
| `//  ^?` | Show the type of the token above the caret (always visible). |
| `//  ^|` | Show completions at the caret. |
| `// @errors: CS0029 CS0103` | The next code line is expected to have these errors; they're shown and don't count as failures. |
| `// @noErrors` | Don't report or fail on any errors. |
| `// ---cut---` (and `---cut-after---`, `---cut-start---` … `---cut-end---`) | Hide setup code; it's still compiled. In C#, top-level statements must come first, so put type declarations after `---cut-after---`. |
| `// @highlight`, `// @focus`, `// @diff: +` / `// @diff: -` | Annotate the next line. |
| `// @log: …`, `// @warn: …`, `// @error: …`, `// @annotate: …` | A callout after the previous line. |
| `#:package Name@1.2.3` | Reference a NuGet package. |

## Accessibility

Hover targets are focusable and carry `aria-describedby` pointing at their
popup (`role="tooltip"`); the popup opens on `:focus-visible`. Popups are pure
CSS, so they can't be dismissed with Escape — move focus or the pointer away.
Pass `focusable: false` to keep tokens out of the tab order.
