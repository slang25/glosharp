## Context

Twohash's MVP supports hover queries (`^?`) and error diagnostics. The data format spec already defines `completions` and `highlights` arrays in the JSON output, but they're always empty. The architecture doc describes a `--region` CLI option and completion (`^|`) markers, but neither is implemented.

The core C# infrastructure (Roslyn compilation, semantic model, marker parsing, line mapping) is in place. `Microsoft.CodeAnalysis.CSharp.Features` is already referenced, which provides `CompletionService`. The `#region` directive is native C# syntax and Roslyn's parser already understands it.

## Goals / Non-Goals

**Goals:**
- Support `^|` completion markers that query Roslyn's CompletionService and return structured completion items
- Support `--region <name>` on the CLI to extract a named `#region` from a source file
- Render completion lists in both Shiki and EC integrations
- Full end-to-end testing for both features

**Non-Goals:**
- Completion filtering/fuzzy matching — we return what Roslyn gives us, unfiltered
- Completion item details/documentation — we return label, kind, and detail, not full resolve
- Nested or overlapping regions — we support the first match of a named region
- Multi-file `@filename` directive support (future work)

## Decisions

### 1. Use AdhocWorkspace + CompletionService for completions

**Decision**: Create an `AdhocWorkspace` with a project and document to use `CompletionService.GetCompletionsAsync()`.

**Alternatives considered**:
- *Manual symbol enumeration via SemanticModel* — `model.LookupSymbols()` at a position. Simpler but produces raw symbols without the filtering, sorting, and ranking that CompletionService provides. Would miss keyword completions, snippet completions, and context-aware suggestions.
- *Language server protocol (LSP)* — overkill for extracting completions at a fixed position.

**Rationale**: CompletionService is what OmniSharp and VS use internally. It produces results that match what developers expect from IntelliSense. The `AdhocWorkspace` can reuse the same references already resolved for compilation, so there's minimal overhead.

### 2. Completion extraction is async — make Process async

**Decision**: Make `TwohashProcessor.Process()` return `Task<TwohashResult>` (async) since `CompletionService.GetCompletionsAsync()` is async.

**Alternatives considered**:
- *Sync wrapper (`.GetAwaiter().GetResult()`)* — works but risks deadlocks and is bad practice.
- *Keep sync, skip completions if none requested* — avoids the breaking change but means we need two code paths.

**Rationale**: The method is only called from the CLI (which can be async) and the Node.js bridge (which already uses async). Making it async is the clean approach. No downstream consumers are affected since the CLI `Program.cs` already uses async patterns.

### 3. Region extraction happens before marker parsing

**Decision**: Extract the region text from the full source file first, then pass the extracted region through the existing marker parsing and compilation pipeline. The full file (not just the region) is compiled so that type information from surrounding code is available.

**Alternatives considered**:
- *Compile only the region* — would lose context from code outside the region (using directives, type definitions, etc.).
- *Post-process the output to filter to region lines* — more complex line mapping, error-prone.

**Rationale**: Compiling the full file ensures accurate type resolution. The region extraction is purely a display concern — it controls what appears in the `code` output, similar to how `---cut---` works. We can implement it as a pre-processing step that identifies the region boundaries, then uses the existing hide/show mechanism to hide everything outside the region.

### 4. Region extraction reuses the hide/show infrastructure

**Decision**: When `--region <name>` is specified, treat everything outside the region as hidden (like `@hide`/`@show`). The `#region` and `#endregion` lines themselves are also hidden.

**Rationale**: This avoids duplicating the line mapping and visibility logic. The existing `MarkerParser` already handles hidden ranges and builds the correct `ProcessedCode` and `LineMap`. We just need to pre-compute which lines are inside the named region.

### 5. Completion items include label, kind, and detail

**Decision**: Each completion item includes `label` (display text), `kind` (symbol kind string), and `detail` (optional type signature).

**Alternatives considered**:
- *Label only* — too sparse for meaningful rendering.
- *Full resolve with documentation* — too expensive for potentially hundreds of items; documentation should be fetched on demand (which isn't our use case).

**Rationale**: Matches the data format spec. Label + kind + detail gives enough information for a useful IntelliSense-style dropdown rendering without excessive data.

## Risks / Trade-offs

- **AdhocWorkspace overhead** — Creating a workspace adds some overhead vs. direct compilation. Mitigation: only create the workspace when `^|` markers are present; reuse the same references.
- **Completion list size** — Roslyn may return hundreds of completion items. Mitigation: the JSON output includes all items; renderers can truncate for display (e.g., show top 10 with "...and N more").
- **Async Process method** — This is a signature change. Mitigation: the only callers are the CLI and tests, both easily updated.
- **Region name ambiguity** — If multiple regions share the same name, we take the first match. Mitigation: document this behavior; duplicate region names are unusual.
