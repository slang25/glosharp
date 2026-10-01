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

**Project**: `src/GloSharp.Core`

**Entry point**: `GloSharpProcessor.ProcessAsync(source, GloSharpProcessorOptions)` returns a
`GloSharpResult` (the JSON contract, see [data-format.md](data-format.md)).
`ProcessWithContextAsync` also returns the `CSharpCompilation` and syntax tree, which `render`
needs for syntax classification (`SyntaxClassifier`) before `HtmlRenderer` produces HTML.

**Inputs**:
- C# source code (string, plus its file path for file-based apps)
- Compilation context: the framework's reference assemblies, a project's `project.assets.json`,
  a file-based app's `#:` directives, or a complog/.glocontext
- Settings: target framework, region, language version, nullable context, implicit usings

#### Processing pipeline

`GloSharpProcessor` is a thin orchestrator. Each stage is an internal type in
`src/GloSharp.Core/Pipeline/`:

```
source ─► Snippet.Prepare ─► result cache? ─► RequestedSettings ─► ICompilationContextProvider
            (directives,       (hit: return)    (langVersion,         .Resolve() → CompilationContext
             region, markers)                    nullable; GS0001/2)          │
                                                                              ▼
GloSharpResult ◄─ Hover / Diagnostic / Completion / Annotation extractors ◄─ CompilationBuilder
```

| Stage | Responsibility |
| --- | --- |
| `Snippet` | Text-only preprocessing, no Roslyn binding: strips `#:` directives (`FileDirectiveParser`), applies the `--region` hidden-line mask (`RegionExtractor`) and parses markers (`MarkerParser`). Immutable; owns the line maps between source, input, compilation and processed (rendered) lines. |
| `RequestedSettings` | The language version / nullable context the snippet asks for (marker beats config). Invalid values become `GS0001`/`GS0002` errors and short-circuit compilation. |
| `ICompilationContextProvider` | Produces one `CompilationContext`: references, base `CSharpParseOptions` (with the TFM's preprocessor symbols) and `CSharpCompilationOptions` (whose language version and nullable context are the defaults), effective TFM, packages, whether ASP.NET Core is referenced, and resolution warnings. Also supplies cheap file fingerprints for the result-cache key. `CompilationContextProviders.Select` picks one, in priority order: `ComplogContextProvider` (complog/.glocontext, keeps the project's own options), `ProjectAssetsContextProvider`, `FileBasedAppContextProvider` (`#:` directives, restored by the SDK), `FrameworkContextProvider`. The last three share `FrameworkReferencesContextProvider`, which adds the shared frameworks and caches the reference list. |
| `CompilationBuilder` | Builds the `CSharpCompilation`: snippet tree + global usings tree, the context's options with the requested settings applied. |
| `HoverExtractor` / `HoverBuilder` | Persistent (`^?`) and automatic hovers; `HoverBuilder` turns a token into its symbol display, overload count and docs (`DocCommentFormatter`, `AnonymousTypeFormatter`). |
| `DiagnosticExtractor` | Classifies diagnostics: visible vs hidden code (`hiddenErrors`), `@errors` expectations, `@noErrors`/`@suppressErrors`, `GS0003` for unmatched expectations, `compileSucceeded`. |
| `CompletionExtractor` | `^|` queries through Roslyn's `CompletionService`, filtered and ordered deterministically. |
| `AnnotationExtractor` | Highlights, custom tags and hidden ranges, straight from the markers. |

**Caching.** Two layers:
- *Result cache* (`--cache-dir`, `ResultCache`): on-disk, keyed by the glosharp version, the whole
  `GloSharpProcessorOptions` record (minus `CacheDir`), the snippet source and the selected
  provider's fingerprints (assets file and project outputs, or the complog file). A hit skips
  context resolution and compilation; `render` then builds the compilation lazily.
- *Context cache* (`CompilationContextCache`): in-memory, per `GloSharpProcessor` (or shared by
  passing one in). Holds reference lists keyed by TFM, packages, SDK, shared frameworks and
  project-output fingerprints, and whole complog/.glocontext contexts keyed by path, project,
  TFM, size and mtime.

**Concurrency.** A `GloSharpProcessor` may process snippets concurrently: providers, compilations
and workspaces are per call, the context cache is a `ConcurrentDictionary` of `Lazy` values (one
factory call per key; failures are not cached), result-cache and directive-stub writes are
atomic (temp file + rename), and the MEF host is a static `Lazy`. A long-lived host should reuse
one processor so references and complogs are resolved once. Project assets are re-read and
file-based apps re-restored on every result-cache miss (so edits are picked up), and the context
cache is unbounded: every rebuilt project output adds a new reference-list entry.

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

# Output JSON to stdout (the only output format)
glosharp process src/Example.cs
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
