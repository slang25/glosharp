# @glosharp/expressive-code

An [Expressive Code](https://expressive-code.com) plugin that adds C# type information to your code blocks: hover popups with signatures and XML docs, `^?` type queries, `^|` completion lists, compiler errors, highlights, diffs and callouts. It's [twoslash](https://twoslash.netlify.app), but for C#.

It works anywhere Expressive Code does: Astro (`astro-expressive-code`), Starlight, `rehype-expressive-code`, or the plain `expressive-code` engine.

## Install

The plugin calls the `glosharp` .NET CLI to compile each snippet with Roslyn. You need the **.NET 8 SDK or later** on the machine that builds your site (including CI).

```sh
# 1. The CLI (pick one; --prerelease while only previews are published)
dotnet tool install --global GloSharp.Cli --prerelease
# ...or as a local tool, in the directory you run your site build from
dotnet new tool-manifest     # only if you don't have .config/dotnet-tools.json yet
dotnet tool install GloSharp.Cli --prerelease

# 2. The plugin
npm install @glosharp/expressive-code
```

> **Pre-1.0 note:** while Glo# is pre-1.0, builds may only be published as prereleases. If the commands above can't find a version, add `--prerelease` to `dotnet tool install` and install the npm package with `npm install @glosharp/expressive-code@alpha`. Keep the CLI and npm package versions in step.

The plugin looks for the CLI in this order: the `executable` option, `glosharp` on your `PATH`, then `dotnet glosharp` (a local tool). If none is found the build fails with install instructions (see [Error handling](#error-handling)).

The package is ESM-only.

## Usage

### Astro and Starlight

Put the plugin in `ec.config.mjs` next to your Astro config (or pass the same object to `expressiveCode({...})` / Starlight's `expressiveCode` option):

```js
// ec.config.mjs
// @ts-check
import { defineEcConfig } from 'astro-expressive-code'
import { pluginGloSharp } from '@glosharp/expressive-code'

export default defineEcConfig({
  plugins: [
    pluginGloSharp({
      cacheDir: 'node_modules/.cache/glosharp',
    }),
  ],
})
```

For Starlight, use `import { defineEcConfig } from '@astrojs/starlight/expressive-code'` instead. The plugin is fully typed, so `astro check` works on a `// @ts-check` config.

### Plain Expressive Code / rehype

```js
import { ExpressiveCode } from 'expressive-code'
import { pluginGloSharp } from '@glosharp/expressive-code'

const ec = new ExpressiveCode({ plugins: [pluginGloSharp()] })
const { renderedGroupAst } = await ec.render({ code: 'var x = 42;', language: 'csharp' })
```

`rehype-expressive-code` takes the same `plugins` array. As with any Expressive Code plugin, include the engine's base styles and JS modules in the page (`ec.getBaseStyles()`, `ec.getThemeStyles()`, `ec.getJsModules()`). The Astro integrations do this for you.

### Writing snippets

Every ```` ```csharp ````, ```` ```cs ```` or ```` ```c# ```` block is compiled. You get hover popups on every meaningful token automatically. Markers add more:

````md
```csharp
var numbers = new[] { 3, 1, 4 };
var biggest = numbers.Max();
//  ^?
```
````

| Marker | Effect |
| --- | --- |
| `//  ^?` | Show the type of the token above the caret, always visible below the line |
| `//      ^\|` | Show a completion list at the caret position |
| `// @errors: CS0029` | Expect these diagnostics on the next code line (comma or space separated). They are rendered, and don't count as unexpected errors |
| `// @noErrors`, `// @suppressErrors[: CS0168]` | Hide (all / listed) diagnostics |
| `// @highlight`, `// @highlight: 2-4` | Highlight the next line or a line range |
| `// @focus`, `// @focus: 2-4` | Dim every other line |
| `// @diff: +`, `// @diff: -` | Mark the next line as added / removed |
| `// @log: …`, `// @warn: …`, `// @error: …`, `// @annotate: …` | Callout boxes below the previous line |
| `// ---cut---`, `// ---cut-after---`, `// ---cut-start---` … `// ---cut-end---` | Compile code that isn't shown |
| `#:package Name@1.2.3` | Reference a NuGet package (file-based app directive) |
| `// @langVersion: 12`, `// @nullable: enable` | Compiler settings for this snippet |

Marker lines are removed from the rendered and copied code.

#### Opting blocks in and out

Fragments that aren't complete programs (`app.MapGet(...)` on its own, for example) would render compiler errors. Opt them out in the code fence meta:

````md
```csharp no-glosharp
builder.Services.AddSingleton<IClock, SystemClock>();
```
````

`glosharp=false` also works. If most of your C# blocks are fragments, flip the default with `explicitTrigger: true`. Then only blocks marked `glosharp` are processed:

````md
```csharp glosharp title="Program.cs"
var x = 42;
```
````

#### Other Expressive Code features

Glosharp composes with Expressive Code's own features: frames and titles, `showLineNumbers`, text and line markers (`"text"`, `ins=`, `del=`, `{1-3}`, `mark={…}`) and `@expressive-code/plugin-collapsible-sections` (`collapse={1-6}`).

**Line ranges like `{1-3}` and `collapse={…}` count lines as written in your markdown**, including marker lines such as `//  ^?`. Ranges that only cover removed marker lines simply disappear. Line numbers shown with `showLineNumbers` count the rendered lines.

## Options

`pluginGloSharp(options)` accepts:

| Option | Type | Default | Description |
| --- | --- | --- | --- |
| `cacheDir` | `string` | — | Directory for the CLI's on-disk result cache. **Recommended**: rebuilds then skip Roslyn for unchanged snippets, which cuts build times a lot (`node_modules/.cache/glosharp` works well and is ignored by git). |
| `explicitTrigger` | `boolean` | `false` | Only process blocks whose meta contains `glosharp`. |
| `onCliError` | `'throw' \| 'warn'` | `'throw'` | What to do when the CLI can't process a block at all. See [Error handling](#error-handling). |
| `failOnErrors` | `boolean` | `false` | Fail the build on unexpected compile errors instead of logging a warning. |
| `project` | `string` | — | Path to a `.csproj`. Its package and project references are available to every snippet. |
| `region` | `string` | — | Show only the named `#region` of each snippet (the rest is compiled but hidden). A block can set or override it with the `region="name"` meta option, e.g. on an `<Code code={source} lang="cs" meta='region="setup"' />`. |
| `framework` | `string` | `net8.0` | Target framework to compile against. |
| `configFile` | `string` | — | Path to a `glosharp.config.json` file (CLI `--config`). By default the CLI looks for one in the working directory and its parents. |
| `complog` | `string` | — | Use a compiler log (`.complog`) for references. |
| `complogProject` | `string` | — | Project to pick from a multi-project complog. |
| `executable` | `string` | — | Path to the `glosharp` CLI. Skips the `PATH` / local tool lookup. |

Any other option of `createGloSharp()` from `@glosharp/core` is passed through to it.

## Error handling

The plugin separates two kinds of problem.

**The CLI can't run** (not installed, crashed, timed out, invalid configuration, a `region` that doesn't exist). By default the build fails with the CLI's error, the document and code block it was processing, and install instructions:

```
glosharp: could not process src/content/docs/intro.md, code block 3 (starting "var x = 42;"):
Failed to spawn glosharp: spawn glosharp ENOENT
To install the glosharp CLI (requires the .NET 8+ SDK):
  ...
```

With `onCliError: 'warn'`, the error is logged once instead, and affected blocks render as plain highlighted code without type information. Their markers are left in place. Use this for local previews on machines without .NET.

**The snippet doesn't compile.** Unexpected errors are always rendered on the page with a wavy underline and a message box below the line. They're also logged as build warnings, with the document, block and line:

```
[WARN] [astro-expressive-code] glosharp: src/pages/posts/errors.md, code block 2 (starting "int count = 3;") has 1 unexpected compile error:
  line 4: CS0029: Cannot implicitly convert type 'string' to 'int'
```

"Unexpected" means:

- error-severity diagnostics not declared with `// @errors:`;
- errors inside hidden (`---cut---`) code, which can't be shown on the page;
- `// @errors:` expectations that never matched.

Warnings and info diagnostics (`CS0219`, `CS8602`…) are rendered, but not logged. Set `failOnErrors: true` to fail the build instead of warning. It's worth turning on in CI:

```js
pluginGloSharp({ failOnErrors: !!process.env.CI, cacheDir: 'node_modules/.cache/glosharp' })
```

Non-fatal problems the CLI reports, such as a package restore failure or a `^?` caret pointing past the end of its line, are logged as warnings too.

## Theming

All colours come from your Expressive Code theme, and switch with it. That covers popups, completion lists, `^?` results, error and callout boxes, and popup syntax colours. Light themes get light popups, and dark themes get dark ones. It works with EC's `useDarkModeMediaQuery` and `themeCssSelector` options, and with Starlight's theme switcher. Popup backgrounds use the theme's `editorHoverWidget.*` colours. Syntax colours inside popups use the theme's token colours, adjusted for contrast.

Override any colour with Expressive Code's `styleOverrides`:

```js
defineEcConfig({
  plugins: [pluginGloSharp()],
  styleOverrides: {
    glosharp: {
      popupBackground: '#1b1f24',
      errorColor: ({ theme }) => theme.colors['editorError.foreground'],
    },
  },
})
```

Available settings (see `GloSharpStyleSettings`):

- **Popups:** `popupBackground`, `popupForeground`, `popupMutedForeground`, `popupBorder`
- **Tokens:** `tokenHoverBackground`, `tokenFocusOutline`
- **Diagnostics:** `errorColor` / `errorBackground`, `warningColor` / `warningBackground`, `infoColor` / `infoBackground`
- **Line states:** `highlightBackground`, `focusDimOpacity`, `diffAddBackground` / `diffAddBorder`, `diffRemoveBackground` / `diffRemoveBorder`
- **Callouts:** `tag{Log,Warn,Error,Annotate}{Color,Background}`
- **Popup syntax:** `syntaxKeyword`, `syntaxType`, `syntaxMethod`, `syntaxProperty`, `syntaxVariable`

Elements carry stable classes for custom CSS:

- `.glosharp-hover`, `.glosharp-popup-container`
- `.glosharp-static` (`^?` results), `.glosharp-completion-list`
- `.glosharp-error-underline`, `.glosharp-error-message`
- `.glosharp-error-expected` (diagnostics declared with `@errors`)
- `.glosharp-tag-{log,warn,error,annotate}`

Block content shown after a line is wrapped in `.glosharp-line > .glosharp-line-extras`.

## Accessibility

- Each code block is a single tab stop. Tabbing onto a hover token shows its popup; <kbd>←</kbd>/<kbd>→</kbd> (or <kbd>↑</kbd>/<kbd>↓</kbd>), <kbd>Home</kbd> and <kbd>End</kbd> move between tokens; <kbd>Esc</kbd> closes the popup; <kbd>Enter</kbd> reopens it.
- Popups have `role="tooltip"` and are linked to their token with `aria-describedby` while open.
- Popups open below the token, or above it near the bottom of the viewport. They stay within the viewport horizontally.
- Tapping a token on touch devices opens its popup; tapping elsewhere closes it.
- The client script registers its listeners once, so it's safe with Astro view transitions and other client-side navigation.

## Caching and performance

Each block starts the `glosharp` CLI once per build. With `cacheDir` set, unchanged snippets come from the on-disk cache on later builds. Snippets with `#:package` directives benefit most, since restore and reference resolution are skipped. Results are also cached in memory for the lifetime of the plugin instance (e.g. during `astro dev`).
