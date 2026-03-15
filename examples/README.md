# Twohash Examples

Sample projects showing how to use twohash in different contexts.

## Prerequisites

All examples require the twohash CLI and the npm packages.

**From the repo root** (uses local packages — no publishing needed):

```bash
# Pack and install the CLI as a local dotnet tool
dotnet pack src/TwoHash.Cli/ -o .nupkg/
dotnet tool restore  # or: dotnet tool install twohash --local --add-source .nupkg/

# Install npm dependencies (workspaces resolve twohash packages locally)
npm install

# Build the TypeScript packages
npm run build --workspace=packages/twohash
npm run build --workspace=packages/shiki
```

Or if the packages are published, just install the CLI globally:

```bash
dotnet tool install -g twohash
```

## Examples

### [`astro-blog/`](./astro-blog/)

An Astro site that uses `@twohash/shiki` to render C# code blocks with hover
type information in Markdown blog posts.

```bash
cd astro-blog
npm install
npm run dev
```

The key integration point is `astro.config.mjs`, which adds a custom Shiki
transformer that calls twohash during Markdown processing.

### [`docusaurus-docs/`](./docusaurus-docs/)

A Docusaurus documentation site with a custom remark plugin that processes C#
code blocks through twohash before rendering.

```bash
cd docusaurus-docs
npm install
npm start
```

See `src/plugins/twohash-plugin.ts` for the remark plugin that hooks into
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

This is the simplest way to try twohash — just write C# with markers and
generate an HTML file.

## Marker Syntax

All examples use the same twohash marker syntax in C# code:

| Marker | Purpose |
|---|---|
| `// ^?` | Show type/hover info at this column |
| `// ^|` | Show IntelliSense completions at this column |
| `// @noErrors` | Assert the snippet compiles cleanly |
| `// @errors: CS0103` | Mark specific errors as expected |
| `// @nullable: enable` | Enable nullable reference types |
| `// ---cut---` | Hide code above from output |
| `// @hide` / `// @show` | Hide/show individual lines |
