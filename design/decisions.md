# Decisions log

Key architectural and design decisions. Each entry captures context, options, and rationale.

---

## 001: CLI-based bridge for build-time integrations

**Scope**: build-time bridges only — a Node process that can spawn children. See [006](#006-gitbook-precomputed-artifacts-not-a-hosted-service) for environments that cannot.

**Context**: GloSharp's core runs on .NET (Roslyn requires it). Shiki/EC integrations run in Node.js. We need a bridge.

**Options considered**:
- **(a) CLI tool** — glosharp CLI outputs JSON, Node.js calls it via child_process
- **(b) WASM** — compile Roslyn to WASM, run in-process in Node.js
- **(c) Native Node addon** — use node-api or similar to load .NET in-process

**Decision: (a) CLI tool**

Rationale: spawning a process is nearly free at build time, and the JSON boundary gives us a clean contract between C# and JS. Native addons add build complexity for no gain here.

**Amended 2026-08-09**: this decision originally rejected WASM on the grounds that "WASM compilation would be extremely difficult and the result would be huge". The first half is wrong: Roslyn is pure managed IL and runs on the stock .NET browser-wasm runtime today (BlazorRepl, Telerik's C# REPL, try.mudblazor.com all compile user C# in-browser). The second half still holds — ~15–30 MB compressed, seconds to first compile under the interpreter. So the reason to prefer the CLI at build time is *cost, not feasibility*, and the choice does not generalise: where no process can be spawned, WASM is on the table (again, [006](#006-gitbook-precomputed-artifacts-not-a-hosted-service)).

---

## 002: Marker syntax — reuse twoslash conventions

**Context**: Twoslash uses markers like `^?` for hover queries and `// @errors: 2322` for expected errors. Should we use the same syntax?

**Options considered**:
- **(a) Reuse twoslash markers** — `^?`, `^|`, `// @errors`, etc.
- **(b) Design C#-specific markers** — e.g., using C# comment conventions
- **(c) Hybrid** — reuse where sensible, extend for C#-specific needs

**Recommendation: (c) Hybrid**

Rationale: Reusing `^?` for hover queries and `// @errors` for expected errors gives familiarity to anyone who knows twoslash. But C# has needs twoslash doesn't: nullable context, using directives that should be hidden, NuGet package declarations. We'll extend with C#-specific markers (e.g., `// @nuget:`, `// @nullable: enable`) while keeping the core syntax compatible.

---

## 003: Require a .csproj for NuGet resolution

**Status**: Resolved

**Context**: Roslyn needs assembly references to produce accurate type information. NuGet packages must be resolved to DLLs.

**Options considered**:
- **(a) Require .csproj** — user maintains a real project, glosharp reads project.assets.json
- **(b) Standalone .cs files** — glosharp resolves packages from custom inline markers
- **(c) File-based apps (.NET 10)** — use `#:package` directives, SDK handles resolution
- **(d) complog** — portable compilation artifact from CI, no SDK needed on docs machine
- **(e) Tiered** — support multiple approaches based on complexity

**Decision: (e) Tiered, with file-based apps as the default path**

.NET 10's [file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) change the equation. The `#:package` directive syntax is first-class SDK support for exactly our use case: single-file C# with inline package declarations. No need to invent custom `// @nuget:` markers.

[complog](https://github.com/jaredpar/complog) by Jared Parsons solves the "portable compilation" problem — CI builds the project, creates a `.complog`, and the docs build consumes it without needing the SDK or NuGet cache.

Tiers:
1. **Simple (no packages)**: standalone .cs with framework refs only
2. **Standard (with packages, .NET 10+)**: file-based apps with `#:package`
3. **Complex (full project)**: .csproj + project.assets.json
4. **Portable (CI-separated)**: complog for when build and docs are different jobs

---

## 004: Integration priority — Expressive Code first

**Context**: We need to decide which rendering integration to build first.

**Options considered**:
- **(a) Expressive Code plugin** — richest feature set, covers Astro/Starlight
- **(b) Shiki transformer** — simpler, covers VitePress and any Shiki user
- **(c) Standalone HTML/CSS** — no dependencies, works everywhere

**Recommendation: (a) Expressive Code plugin**

Rationale: Expressive Code provides the richest rendering capabilities (frames, annotations, styles, client-side JS). Building the EC plugin first means we design the data format for the hardest integration, and simpler integrations (Shiki transformer, standalone) become subsets. Starlight's growing popularity also makes this the highest-impact first target.

However, the core data format should be designed to work with all three from day one.

---

## 005: CSS anchor positioning for tooltips

**Context**: How should hover tooltips be positioned in the rendered output?

**Options considered**:
- **(a) CSS anchor positioning** — modern CSS, no JS, clean markup
- **(b) JavaScript positioning** (Floating UI / Popper) — works everywhere, more complex
- **(c) CSS-only with absolute positioning** — no anchor API, manual calc

**Recommendation: (a) CSS anchor positioning**

Rationale: The user specified targeting modern browsers. CSS anchor positioning is supported in Chrome 125+, Edge 125+, and Firefox 131+ (2024). By the time glosharp ships and is adopted, support will be widespread. The markup is cleaner and there's no JS dependency. We can provide a simple CSS fallback for older browsers if needed.

---

## 006: GitBook — precomputed artifacts, not a hosted service

**Status**: Resolved

**Context**: GitBook is a hosted renderer. There is no markdown/build pipeline to hook, no raw HTML in content, and the one extension point — a ContentKit integration — runs in a Workers-style sandbox with no child processes and no filesystem. Roslyn cannot run there, so the compile has to happen somewhere else. Where?

**Options considered**:
- **(a) CI precompute + static artifacts** — the repo's own CI runs `glosharp render` over every fence and publishes `sha256(code).html` fragments to static hosting; the block looks each one up by hashing the fence body it was handed
- **(b) .NET WASM inside the webframe** — GloSharp.Core + Roslyn on the browser runtime, ref assemblies as static assets
- **(c) Hosted GloSharp API** — an ASP.NET service wrapping `GloSharpProcessor`, called from the webframe

**Decision: (a), with (b) as a possible later addition for editor preview. (c) rejected.**

(c) is the tempting one and the wrong one: it means operating a service, and exposing an endpoint that compiles arbitrary submitted C#, to enable nothing that (a) and (b) do not already cover.

(a) needs no infrastructure at all — CI already has a .NET SDK, and a committed `.glocontext` makes the compilation context cheap and SDK-free. Content addressing is what makes it work: CI and the reader's browser never have to agree on anything but the snippet text, so there is no manifest to keep in sync and no per-page coordination.

The cost is staleness — a snippet renders as plain code until CI publishes it, including while an author is editing it in GitBook. (b) is the fix for the editor specifically, and is now known to be feasible (see [001](#001-cli-based-bridge-for-build-time-integrations)); the tiers a GitBook integration needs (bare snippet + framework refs, `.glocontext`) are pure `GloSharpProcessor` + `MetadataReference` with no MSBuild and no SDK. It is deferred rather than rejected because it is a real chunk of work — byte-array reference loading (`CreateFromImage` instead of `CreateFromFile`), a browser build target, ref assemblies as static assets — whose value depends on a latency measurement nobody has taken. ~15–30 MB and seconds to first compile is defensible for an author editing one snippet and indefensible per reader page-load, which is why it could only ever be the editor path, never the reading path.

**Lookup happens in the browser, not in the integration's `fetch` handler.** Resolving artifacts worker-side would let us cache in Workers and would sidestep CORS, but it would also turn the integration into an open GET proxy that inlines arbitrary fetched HTML onto its own origin. Fetching from the frame instead costs a CORS requirement on the artifacts host (GitHub Pages, the recommended target, sends `Access-Control-Allow-Origin: *`) and nothing else.

**The fence is `glosharp`, not `csharp`.** A custom block can claim any fence string, but claiming `csharp` would take over every C# fence in every space the integration is installed on, wanted or not — and would make the snippets invalid on every other renderer.
