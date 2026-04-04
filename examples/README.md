# GloSharp Examples

Sample projects showing how to use glosharp in different contexts.

## Prerequisites

All examples require the glosharp CLI and the npm packages.

**From the repo root** (uses local packages — no publishing needed):

```bash
# Pack and install the CLI as a local dotnet tool
dotnet pack src/GloSharp.Cli/ -o .nupkg/
dotnet tool restore  # or: dotnet tool install glosharp --local --add-source .nupkg/

# Install npm dependencies (workspaces resolve glosharp packages locally)
npm install

# Build the TypeScript packages
npm run build --workspace=packages/glosharp
npm run build --workspace=packages/shiki
```

Or if the packages are published, just install the CLI globally:

```bash
dotnet tool install -g glosharp
```

## Examples

### [`astro-blog/`](./astro-blog/)

An Astro site that uses `@glosharp/shiki` to render C# code blocks with hover
type information in Markdown blog posts.

```bash
cd astro-blog
npm install
npm run dev
```

The key integration point is `astro.config.mjs`, which adds a custom Shiki
transformer that calls glosharp during Markdown processing.

### [`expressive-code/`](./expressive-code/)

An Astro site that uses `astro-expressive-code` with the `@glosharp/expressive-code`
plugin. This is the simplest framework integration — just add `pluginGloSharp()` to
your Expressive Code config and all C# code blocks get type hovers automatically.

```bash
cd expressive-code
npm install
npm run dev
```

The key integration point is `astro.config.mjs`, which passes the glosharp plugin
to `astro-expressive-code`.

### [`docusaurus-docs/`](./docusaurus-docs/)

A Docusaurus documentation site with a custom remark plugin that processes C#
code blocks through glosharp before rendering.

```bash
cd docusaurus-docs
npm install
npm start
```

See `src/plugins/glosharp-plugin.ts` for the remark plugin that hooks into
Docusaurus's Markdown pipeline.

### [`standalone/`](./standalone/)

A minimal Node.js script that reads `.cs` files and generates a self-contained
HTML page with interactive hover popups. No framework required.

```bash
cd standalone
npm install
npm run render
open output.html
```

This is the simplest way to try glosharp — just write C# with markers and
generate an HTML file.

## Marker Syntax

All examples use the same glosharp marker syntax in C# code:

| Marker | Purpose |
|---|---|
| `// ^?` | Show type/hover info at this column |
| `// ^|` | Show IntelliSense completions at this column |
| `// @noErrors` | Assert the snippet compiles cleanly |
| `// @errors: CS0103` | Mark specific errors as expected |
| `// @nullable: enable` | Enable nullable reference types |
| `// ---cut---` | Hide code above from output |
| `// @hide` / `// @show` | Hide/show individual lines |
