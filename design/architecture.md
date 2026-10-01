# Architecture

## System overview

GloSharp is split into layers: a .NET core that does the heavy lifting with Roslyn, a CLI interface that produces JSON, and thin JS integrations that consume that JSON for rendering.

```
                          Build time
                    ┌─────────────────────┐
                    │                     │
  .cs files ──────►│   glosharp core      │──────► JSON metadata
  .csproj ────────►│   (C# / Roslyn)     │
                    │                     │
                    └─────────────────────┘
                              │
                         glosharp CLI
                              │
                       JSON on stdout
                              │
              ┌───────────────┼───────────────┐
              │               │               │
              ▼               ▼               ▼
      Shiki transformer   EC plugin    Standalone renderer
      (Node.js)          (Node.js)     (HTML/CSS, no JS deps)
              │               │               │
              ▼               ▼               ▼
         HAST nodes      EC annotations   Static HTML
              │               │               │
              └───────┬───────┘               │
                      ▼                       ▼
              Doc framework output      Anywhere (Hugo, etc.)
              (Astro, VitePress)
```

## Component details

### 1. GloSharp Core (.NET library)

**Package**: `GloSharp.Core` (or similar)

**Responsibilities**:
- Parse C# source code via Roslyn
- Create `CSharpCompilation` with appropriate references
- Walk syntax tree, extract symbol metadata at marker positions
- Process glosharp marker syntax (`^?`, `// @errors`, etc.)
- Produce structured metadata output
- Report diagnostics (compile errors)

**Key APIs used**:
- `CSharpSyntaxTree.ParseText()` — parse source
- `CSharpCompilation.Create()` — create compilation context
- `SemanticModel.GetSymbolInfo()` — resolve symbols
- `ISymbol.ToDisplayString()` — format type info for display
- `Compilation.GetDiagnostics()` — get compile errors

**Inputs**:
- C# source code (string or file path)
- Compilation context: assembly references, either from a .csproj/project.assets.json or default framework refs

**Output**: `GloSharpResult` object (serialized as JSON)

### 2. GloSharp CLI

**Package**: `GloSharp.Cli` dotnet tool (global or local)

**Usage**:
```bash
# Process a single file
glosharp process src/Example.cs

# Process a specific region
glosharp process src/Example.cs --region getting-started

# Process with a project context
glosharp process src/Example.cs --project src/Example.csproj

# Verify snippets compile (CI mode); files and/or directories
glosharp verify samples/ docs/intro.cs

# Render HTML instead of JSON
glosharp render src/Example.cs --standalone --output example.html
```

**Responsibilities**:
- Parse CLI arguments
- Resolve project context (find .csproj, resolve NuGet packages)
- Call core library
- Output JSON to stdout (for piping to JS integrations)
- `verify` exits non-zero on unexpected errors (for CI). `process` and `render` exit 0 whenever
  they produce a result: `meta.compileSucceeded` carries the compile status

### 3. Node.js bridge

**Package**: `@glosharp/core` (npm)

**Responsibilities**:
- Spawn `glosharp` as child process
- Parse JSON output
- Provide typed TypeScript API for integrations
- Cache results during a build

```typescript
import { createGloSharp } from '@glosharp/core'

const glosharp = createGloSharp({
  // Path to dotnet tool, or auto-detect
  executable: 'glosharp',
})

const result = await glosharp.process({
  code: 'var x = 42;',
  // or: file: 'src/Example.cs',
  // or: file: 'src/Example.cs', region: 'snippet-name',
  project: 'src/Example.csproj', // optional
})

// result.hovers, result.errors, result.code, etc.
```

### 4. Shiki transformer

**Package**: `@glosharp/shiki` (npm)

Shiki's transformer hooks are synchronous and glosharp has to run the compiler, so unlike
`@shikijs/twoslash` it works in two steps: process the blocks first, then let the transformer
look each result up by its code.

```typescript
import { processGloSharpBlocks, transformerGloSharpFromMap } from '@glosharp/shiki'

const results = await processGloSharpBlocks([code], { project: 'src/Example.csproj' })

const html = await codeToHtml(code, {
  lang: 'csharp',
  themes: { light: 'github-light', dark: 'github-dark' },
  transformers: [transformerGloSharpFromMap(results)],
})
```

Popup styling ships as `@glosharp/shiki/style.css`.

### 5. Expressive Code plugin

**Package**: `@glosharp/expressive-code` (npm)

```typescript
import { pluginGloSharp } from '@glosharp/expressive-code'

export default defineConfig({
  integrations: [
    starlight({
      expressiveCode: {
        plugins: [pluginGloSharp()],
      },
    }),
  ],
})
```

### 6. Standalone renderer

**Package**: part of `glosharp` npm package or separate

For environments without Shiki/EC (Hugo, Jekyll, custom builds):

```bash
glosharp render src/Example.cs --theme github-dark > output.html
```

Produces self-contained HTML with inline CSS. Uses CSS anchor positioning for hover tooltips.

## Data flow for a typical doc build

1. **Author** writes C# in a sample project with `#region` markers
2. **Doc framework** (e.g., Starlight) processes markdown, encounters a code block referencing the sample
3. **GloSharp plugin** calls the CLI with the source file and region
4. **GloSharp CLI** loads the .csproj, resolves references, compiles with Roslyn, extracts metadata
5. **JSON metadata** flows back to the plugin
6. **Plugin** maps metadata to HAST nodes (Shiki) or annotations (EC)
7. **Rendered HTML** includes hover tooltips, error markers, type information

## Key design principles

- **The JSON boundary is the contract** — everything downstream of the CLI is a thin adapter
- **Fail loud in CI** — compile errors should break the build
- **Framework-agnostic core** — the .NET library knows nothing about Shiki or EC
- **Build-time only** — no runtime JS required in the rendered output (CSS anchor positioning)
