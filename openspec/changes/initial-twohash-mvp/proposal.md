## Why

C# documentation lacks compiler-derived type information. TypeScript has twoslash, which extracts real type info from code snippets to render hover tooltips, verified compilation, and rich error annotations in docs. C# has nothing equivalent — authors either manually annotate code or ship plain syntax-highlighted blocks that silently drift from reality. Twohash brings this capability to C# using Roslyn.

## What Changes

- Introduce a new .NET library (Twohash Core) that parses twoslash-style markers from C# code and uses Roslyn to extract hover info, diagnostics, and structured metadata
- Introduce a CLI tool (`twohash`) that wraps the core library, accepts C# source, and outputs structured JSON to stdout
- Introduce a Node.js bridge package that spawns the CLI and provides a typed TypeScript API
- Introduce a Shiki transformer that injects hover popups and error annotations into code blocks
- Introduce an Expressive Code plugin (primary integration target) with theme-aware styling and CSS anchor positioning
- Define the JSON data format contract between the .NET core and JS rendering layers

## Capabilities

### New Capabilities
- `marker-parsing`: Parse twoslash-compatible markers (`^?`, `// @errors:`, `// @noErrors`, cut markers, `// @hide`) from C# source and build position offset maps
- `roslyn-extraction`: Compile C# with Roslyn using framework reference assemblies, extract hover info via `ToDisplayParts()`, diagnostics, and XML doc comments
- `json-output`: Structured JSON output format (hovers, errors, highlights, hidden sections, meta) as the contract between .NET and JS layers
- `cli-tool`: Dotnet tool that accepts source input, resolves framework refs, invokes core, and outputs JSON to stdout
- `node-bridge`: TypeScript package that spawns CLI, parses JSON output, provides typed API with caching
- `shiki-transformer`: Shiki transformer using preprocess/root hooks to inject hover popups and error annotations via CSS anchor positioning
- `expressive-code-plugin`: Expressive Code plugin with preprocessCode/annotateCode/postprocessRenderedBlock hooks, theme-aware styling, and CSS anchor-positioned tooltips

### Modified Capabilities

## Impact

- New .NET solution with core library and CLI tool projects (targeting .NET 8+)
- New npm packages: `twohash` (bridge), `@twohash/shiki` (transformer), `@twohash/expressive-code` (plugin)
- Dependencies: Microsoft.CodeAnalysis.CSharp, Microsoft.CodeAnalysis.CSharp.Features
- Requires .NET SDK installed for framework reference assembly resolution
- MVP scope: standalone mode only (framework refs, no NuGet resolution)
- Browser requirement for rendered output: CSS anchor positioning support (Chrome 125+, Firefox 131+, Safari 26+)
