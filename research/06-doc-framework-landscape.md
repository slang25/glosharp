# Documentation framework landscape (2025)

## The big picture

C# developers writing docs and blogs overwhelmingly use JavaScript-based static site generators. There is no dominant .NET-native SSG. This means glosharp's integrations should target the JS ecosystem.

## Framework comparison

### Astro / Starlight

**Position**: Fastest-growing SSG, Starlight is its dedicated docs theme.

- Island architecture, ships zero JS by default
- **Expressive Code built-in** — first-class code block rendering
- MDX and Markdoc support
- Strong plugin ecosystem
- Used by: Astro docs, many OSS projects

**Code snippet features**:
- Expressive Code handles all code rendering (syntax highlighting, frames, copy button, text markers)
- Supports file imports via MDX components
- Shiki under the hood

**Integration opportunity**: Expressive Code plugin (highest priority — Starlight users get it for free).

### VitePress

**Position**: Dominant for Vue ecosystem, popular for general technical docs.

- Vue + Vite powered
- **Shiki built-in** with transformer support
- `<<< @/filepath` syntax for file imports with line ranges and regions
- Code groups (tabbed code blocks)
- Twoslash already integrated

**Code snippet features**:
- Line highlighting via `// [!code highlight]`
- Diff markers via `// [!code --]` / `// [!code ++]`
- Focus via `// [!code focus]`
- External file imports with region support

**Integration opportunity**: Shiki transformer (VitePress users can add it directly).

### Docusaurus

**Position**: Mature, widely used for OSS documentation.

- React-based
- Uses Prism for syntax highlighting (not Shiki by default)
- MDX support
- Live code editor for JS/JSX
- Line highlighting via comments or metadata

**Code snippet features**:
- `// highlight-next-line` comment syntax
- Magic comments for various annotations
- Code tabs for multi-language examples

**Integration opportunity**: Lower priority. Would need either a Prism plugin or a switch to Shiki (which is possible but not default).

### Hugo

**Position**: Fastest build times, Go-based.

- Uses Chroma for syntax highlighting
- Minimal code enhancement features
- Popular with individual bloggers

**Integration opportunity**: Low — different syntax highlighting engine, less plugin-friendly.

### Docfx

**Position**: Microsoft's official .NET documentation tool.

- Purpose-built for C# API documentation
- First-class `[!code-csharp[]]` syntax with region support
- Generates API reference from XML doc comments
- Used for learn.microsoft.com

**Integration opportunity**: Docfx already has its own code handling. GloSharp could add hover information that Docfx doesn't currently provide, but the integration surface is different from Shiki/EC.

## Where C# developers blog

Based on the ecosystem:
- **Individual blogs**: Hugo, Jekyll (GitHub Pages), Astro, or hosted platforms (dev.to, Medium)
- **OSS project docs**: Docfx, Starlight, VitePress, Docusaurus
- **Slides**: Slidev (has twoslash support), reveal.js, or PowerPoint

Most C# bloggers use whatever SSG they're comfortable with. There's no "default C# blogging tool."

## Integration priority recommendation

1. **Expressive Code plugin** — covers Astro/Starlight users, framework-agnostic, richest feature set
2. **Shiki transformer** — covers VitePress, Nuxt, any Shiki user; follows established twoslash pattern
3. **Standalone HTML/CSS renderer** — no dependencies, works anywhere (Hugo, Jekyll, custom)
4. **Docfx integration** — if there's demand from .NET OSS maintainers

The EC plugin and Shiki transformer can share the same core data format — the difference is just how they render it.

## Key insight

The doc framework doesn't matter much for glosharp's core design. The C# → JSON metadata pipeline is framework-agnostic. What matters is having clean integration points at the rendering layer. Design the data format right, and framework integrations become thin adapters.
