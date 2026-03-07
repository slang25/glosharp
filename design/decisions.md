# Decisions log

Key architectural and design decisions. Each entry captures context, options, and rationale.

---

## 001: CLI-based bridge between C# core and JS integrations

**Context**: Twohash's core runs on .NET (Roslyn requires it). Shiki/EC integrations run in Node.js. We need a bridge.

**Options considered**:
- **(a) CLI tool** — twohash CLI outputs JSON, Node.js calls it via child_process
- **(b) WASM** — compile Roslyn to WASM, run in-process in Node.js
- **(c) Native Node addon** — use node-api or similar to load .NET in-process

**Recommendation: (a) CLI tool**

Rationale: Roslyn is a large, complex runtime — WASM compilation would be extremely difficult and the result would be huge. Native addons add build complexity. A CLI tool is simple, debuggable, and the latency of spawning a process is acceptable since this runs at build time, not in a hot path. The JSON boundary also gives us a clean contract between C# and JS.

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
- **(a) Require .csproj** — user maintains a real project, twohash reads project.assets.json
- **(b) Standalone .cs files** — twohash resolves packages from custom inline markers
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

Rationale: The user specified targeting modern browsers. CSS anchor positioning is supported in Chrome 125+, Edge 125+, and Firefox 131+ (2024). By the time twohash ships and is adopted, support will be widespread. The markup is cleaner and there's no JS dependency. We can provide a simple CSS fallback for older browsers if needed.
