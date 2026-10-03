# Glo# website

The project's landing page: an Astro site whose C# examples are rendered by Glo# itself through
`@glosharp/expressive-code`.

## Layout

| Path | What |
| --- | --- |
| `src/pages/index.astro` | The whole page: features, integrations, setup tabs, marker reference |
| `src/examples/*.cs` | The C# examples. Each feature card renders one of these files, and its "Source" tab shows the same file. |
| `ec.config.mjs` | Expressive Code config with `pluginGloSharp()` |
| `astro.config.mjs` | Astro config; reloads the page when an example `.cs` file changes |

## Develop

From the repository root:

```sh
npm ci
npm run cli:build
export GLOSHARP_EXECUTABLE="$PWD/src/GloSharp.Cli/bin/Release/net8.0/GloSharp.Cli"

npm run dev -w website       # http://localhost:4321
npm run build -w website     # writes website/dist
```

`predev`/`prebuild` build `@glosharp/core` and `@glosharp/expressive-code` first. Without
`GLOSHARP_EXECUTABLE`, the plugin looks for `glosharp` on `PATH`.

## Rules for examples

- Every file in `src/examples/` must pass `glosharp verify` (`npm run verify:website` from the
  root; CI runs it). Declare intended errors with `// @errors:`; don't hide them with
  `@noErrors` or `@suppressErrors`.
- Code shown on the page (setup snippets, the marker reference) must match the current packages
  and CLI. When an API changes, update it here too.

## Deploy

`.github/workflows/deploy-website.yml` builds the site and deploys `website/dist` to Cloudflare
Pages after CI passes on `main`. It installs the CLI from source, so the site always reflects the
current code.
