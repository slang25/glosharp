## Context

TwoHash currently requires explicit `^?` markers to extract and display type information. The extraction happens in `TwohashProcessor.ExtractHovers()` which iterates only over `markers.HoverQueries` — positions explicitly marked with `^?`. The expressive-code plugin only processes C# blocks that contain twohash markers, and renders all hovers identically as CSS-anchored popups visible on `:hover`.

The change makes type information available on all tokens by default, and repurposes `^?` as a "persistent hover" (always visible).

## Goals / Non-Goals

**Goals:**
- Every semantically meaningful token in a twohash-processed code block should have hover data available
- `^?` markers produce persistent (always-visible) hovers, visually distinct from default mouse-over hovers
- All C# code blocks are processed by the plugin (not just those with markers)
- Output format distinguishes persistent vs default hovers

**Non-Goals:**
- Hover data for non-identifier tokens (punctuation, whitespace, operators without semantic meaning)
- JavaScript-based interaction — continue with CSS-only approach
- Changes to completion (`^|`) or error (`@errors`) behavior
- Server-side or lazy-loading of hover data — all data is embedded at build time

## Decisions

### 1. Add `ExtractAllHovers()` alongside existing `ExtractHovers()`

Extract hover data for all identifier tokens by walking the syntax tree with `root.DescendantTokens()`, filtering to tokens that resolve to a symbol.

**Rationale:** Reuses the existing symbol resolution and display logic. Walking `DescendantTokens()` is straightforward with Roslyn and naturally scopes to tokens (not trivia). The existing `ExtractHovers()` method can be refactored to share the core symbol-to-hover logic.

**Alternative considered:** Using `SemanticModel.GetSymbolInfo()` on every node — rejected because tokens are the right granularity (nodes can be nested and would produce duplicates).

### 2. Add `Persistent` boolean flag to `TwohashHover`

Add `public bool Persistent { get; init; }` to the hover model. Default hovers have `persistent: false`, `^?`-triggered hovers have `persistent: true`.

**Rationale:** Minimal model change, backward-compatible in JSON (defaults to false if absent). The plugin can trivially branch on this flag for rendering. Simpler than separate lists or a `kind` enum.

**Alternative considered:** Separate `hovers` and `persistentHovers` arrays — rejected as it duplicates structure and complicates consumers.

### 3. Token filtering strategy for auto-hovers

Only extract hovers for tokens where `SemanticModel.GetSymbolInfo()` or `GetDeclaredSymbol()` resolves to a non-null symbol. Skip tokens that are:
- Pure syntax (braces, semicolons, commas)
- Keywords without semantic binding (`var` before resolution, `using`, `namespace`, `class` as keywords)
- String literals and numeric literals (no useful hover data)

**Rationale:** Keeps output size manageable. A 10-line snippet might have ~15-30 meaningful tokens vs 50+ total tokens. Only tokens with actual type/symbol info provide value.

**Note:** `var` as an inferred type keyword _does_ resolve to a symbol (the inferred type), so it will get hover data — this is desirable.

### 4. Plugin processes all C# blocks

Remove the marker-detection gate. All C# code blocks are sent through twohash processing.

**Rationale:** With auto-hovers, every C# block benefits from processing. The marker check becomes unnecessary overhead.

**Trade-off:** Increases build time for docs with many C# blocks. Mitigated by the existing result cache.

### 5. Two CSS rendering modes for hovers

- **Default hovers**: Token gets a `twohash-hover` wrapper. Popup appears on `:hover` (same as today). No underline/decoration on the token — it should look like normal code until hovered.
- **Persistent hovers**: Token gets `twohash-hover twohash-hover-persistent` wrapper. Popup is always visible (no `:hover` gate). Styled with a subtle underline on the token to indicate the pinned annotation (similar to today's `^?` rendering).

**Rationale:** Default hovers should be invisible until interaction — adding underlines to every token would be visually noisy. Persistent hovers keep today's visible annotation style.

### 6. Position mapping for auto-extracted hovers

Auto-extracted hovers use compilation-code positions, which must be mapped back to processed-code positions using the existing `LineMap`. The same `FindCompilationLine` reverse-mapping logic applies.

**Rationale:** Reuses proven position-mapping infrastructure. No new mapping concepts needed.

## Risks / Trade-offs

- **Output size increase** → Auto-hovers significantly increase JSON payload size. Mitigation: only extract for tokens with resolved symbols; the result cache prevents re-computation.
- **Build time increase** → Processing all C# blocks and extracting all hovers is more expensive. Mitigation: result cache; consider a plugin option to opt-out of auto-hovers for specific blocks.
- **Breaking change for `^?` users** → Existing `^?` markers change from "show hover" to "show persistent hover". Mitigation: document in release notes; behavior is arguably better (persistent is more intentional than on-demand).
- **HTML size increase** → Every token with hover data gets wrapped in a span with popup HTML. Mitigation: Consider lazy popup rendering (inject data attributes, generate popup HTML via CSS `content` or deferred injection).
