## Context

C# documentation tooling lacks compiler-driven type information extraction. TypeScript's twoslash provides this for TS — hover tooltips, verified compilation, error annotations — but nothing equivalent exists for C#. Roslyn provides the compiler APIs needed, and the JS doc framework ecosystem (Shiki, Expressive Code) has established plugin patterns via twoslash that twohash can follow.

The project has extensive research docs covering twoslash architecture, Roslyn APIs, Shiki/EC integration patterns, NuGet resolution strategies, and the doc framework landscape. This design synthesizes those into an implementable MVP.

## Goals / Non-Goals

**Goals:**
- Extract hover info, diagnostics, and doc comments from C# code using Roslyn
- Output structured JSON matching the data format spec for consumption by JS integrations
- Provide a CLI tool usable from Node.js child processes
- Ship working Shiki transformer and Expressive Code plugin with CSS anchor-positioned tooltips
- Support standalone compilation using framework reference assemblies (no NuGet)
- Fail CI builds on unexpected compilation errors

**Non-Goals:**
- NuGet package resolution (future: file-based apps, .csproj + project.assets.json, complog)
- Completions support (`^|` markers)
- Standalone HTML renderer (CLI `render` command)
- Docfx integration
- Runtime JavaScript in rendered output
- Source generator / analyzer support

## Decisions

### D1: Monorepo with .NET solution + npm workspaces

The .NET core/CLI and the npm packages live in one repo. The .NET solution contains `TwoHash.Core` (class library) and `TwoHash.Cli` (dotnet tool). The npm side uses workspaces for `twohash` (bridge), `@twohash/shiki`, and `@twohash/expressive-code`.

**Why**: Single repo simplifies development, testing, and versioning. The JSON contract is the boundary — both sides can evolve independently as long as the schema holds.

### D2: CLI-based bridge over WASM or native addons

The Node.js bridge spawns `twohash` CLI as a child process and parses JSON from stdout. Alternatives considered: compiling Roslyn to WASM (too complex, Roslyn depends heavily on .NET runtime), native Node.js addon via NativeAOT (fragile cross-platform, complex build).

**Why**: CLI is the simplest reliable bridge. Build-time latency (~200-500ms per snippet) is acceptable. JSON stdout is universally parseable. The CLI is independently useful.

### D3: Framework reference assemblies for MVP compilation

For the initial version, twohash resolves framework reference assemblies from the installed .NET SDK (`~/.dotnet/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/`). No NuGet packages, no .csproj loading.

**Why**: Zero friction for simple snippets. Most doc examples use only framework types. NuGet support is the hardest problem and can be layered on later without changing the core architecture.

### D4: Marker syntax compatible with twoslash

Reuse twoslash conventions: `^?` for hover queries, `// @errors: NNNN` for expected errors, `// @noErrors` for clean compilation assertion, `---cut---` for cut markers, `// @hide`/`// @show` for hidden sections. Position-based column alignment for `^?`.

**Why**: Familiarity for developers already using twoslash. The marker syntax is well-proven. C#-specific extensions (`// @nullable:`, `// @using:`) can be added later without breaking compatibility.

### D5: CSS anchor positioning for tooltips

Hover popups use CSS anchor positioning (`anchor-name`, `position-anchor`, `inset-area`). No JavaScript for tooltip positioning. Shown/hidden via `:hover` pseudo-class.

**Why**: All target browsers support it (Chrome 125+, Firefox 131+, Safari 26+). Eliminates JS dependency. Simpler markup. Aligns with "build-time only, no runtime JS" principle.

### D6: Expressive Code plugin as primary integration

The EC plugin is the richest integration (theme-aware styling, annotation system, hook lifecycle). Designing for EC first ensures simpler integrations (Shiki transformer) work as subsets.

**Why**: Starlight/Astro is the fastest-growing doc framework and ships EC built-in. The EC plugin model is more capable than raw Shiki transformers. Building the more complex integration first de-risks the simpler ones.

### D7: Position mapping via offset array

After removing marker lines, maintain an array mapping processed-code line numbers to original-code line numbers. All output positions reference the processed code. The original code is preserved in the JSON for debugging.

**Why**: Same approach as twoslash. Consumers only work with clean code positions. The offset map is an internal detail of the core library.

## Risks / Trade-offs

- **[CLI startup latency]** → Each snippet invocation starts a new .NET process. Mitigation: the bridge caches results during a build; future optimization could use a long-running server mode.
- **[Framework ref discovery]** → Finding the right SDK packs directory is platform-dependent. Mitigation: `dotnet --info` can locate the SDK; fall back to well-known paths; fail clearly if not found.
- **[CSS anchor positioning browser support]** → Older browsers won't show tooltips. Mitigation: graceful degradation (tooltips simply don't appear); the code block itself is always readable.
- **[Roslyn version coupling]** → The core library's Roslyn dependency must match the target C# language version. Mitigation: target the current LTS (.NET 8) with latest Roslyn for broadest language support.
- **[No NuGet in MVP]** → Snippets using third-party types won't compile. Mitigation: clear error messaging; document the limitation; NuGet support is the planned next phase.
