# Design: Fix Rendering Defects

## Context

Five defects found by the rendering feedback loop (`tests/rendering/`), each currently pinned by a `test.fail` marker or a `KNOWN_ISSUES` gallery skip. Two crash the EC render (completions, multi-line error messages), three degrade output (static popup overlap, mobile popup overflow, Shiki severity/fallback). All fixes are render-side: no CLI, JSON format, or fixture changes.

## Goals / Non-Goals

**Goals:**
- Every fixture renders through the EC engine without crashing; the two `KNOWN_ISSUES` skips are removed.
- The `test.fail` markers come off — the invariants that documented the defects now pass as plain assertions.
- No regression in the 54-test invariant suite or package unit tests.

**Non-Goals:**
- Redesigning popup visuals or the static-annotation look (only layout/positioning correctness).
- Keyboard accessibility for popups (separate future change).
- Touching the standalone C# HTML renderer (its severity styling and `@supports` fallback already exist).

## Decisions

### 1. EC annotations use the wrap-and-nest render pattern (defects 1–2)
EC core requires render output length === input length. The single-line error annotation already complies by nesting its message box inside the wrapped node — apply the same idiom everywhere:
- `GloSharpCompletionAnnotation` drops its zero-width `inlineRange` and becomes a full-line annotation; render wraps the line's nodes in a container and nests the `<ul>` after them inside it, returning one node per input node.
- `GloSharpErrorAnnotation` with `messageOnly` wraps the received nodes and nests the message box inside the wrapper of the last node instead of appending a sibling.
- *Alternative considered:* patching around EC core's validator (e.g. rendering via `postprocessRenderedBlock` DOM surgery). Rejected — the validator encodes EC's real contract; conforming is simpler and survives EC upgrades.

### 2. Static popups join normal flow (defect 3)
`.glosharp-static > .glosharp-popup-container` switches from absolute overlay to block flow inside the `.glosharp-noline` line wrapper (position: static, block, with vertical margins). EC lines are block-level, so the extra height simply flows and following lines move down.
- *Alternative considered:* keep overlays and JS-measure offsets. Rejected — fragile, reflows on font load, and still overlaps at the wrong zoom.
- *Verify:* gallery screenshot of `ec/local-variables` (6 static popups) plus a new no-overlap invariant asserting no static container's box intersects any code line's or other container's box.

### 3. EC hover popup clamping is CSS max-width + JS position clamp (defect 4)
- CSS: `max-width: min(560px, calc(100vw - 24px))`; docs sections wrap normally; the signature line uses `pre-wrap` so a constrained popup wraps instead of overflowing.
- JS: `positionPopup()` clamps the computed left so the popup's viewport-space edges stay within `[8, viewportWidth - 8]`, after the popup is displayed (so `offsetWidth` is real).
- **Invariant interplay:** when clamped, `popup.left === token.left` no longer holds. `expectEcAdjacent` gains a clamped mode: horizontal alignment OR (popup touches a viewport edge AND the token's x-range intersects the popup's x-range). Vertical adjacency is unchanged.

### 4. Shiki popup becomes a child of the hover wrapper (defect 5b)
`wrapTokenAtPosition` places the popup inside `<span class="glosharp-hover">` instead of after it. This buys three things at once: an anchor point for the `@supports not (anchor-name: --a)` fallback (`position: absolute; bottom: calc(100% + 4px); left: 0` relative to the wrapper), a gap-free hover contract (`.glosharp-hover:hover > .glosharp-popup` keeps the popup open while the pointer is on it — no more racing `display:none` across the 4px margin), and simpler injection (no sibling splice).
- *Trade-off:* in fallback mode the absolute popup is clipped by the `<pre>`'s `overflow-x: auto` — accepted as degraded-but-usable; anchor-positioned browsers are unaffected (`position: fixed` escapes clipping).
- *Consumer impact:* anyone who wrote `.glosharp-hover + .glosharp-popup` selectors must switch to `>` — called out in the changelog; `@glosharp/shiki/style.css` remains the supported styling entry point and is updated in the same commit.

### 5. Shiki severity colors are a straight compliance fix (defect 5a)
Add `.glosharp-severity-warning` / `.glosharp-severity-info` rules (underline color, message color/background/border) to `style.css`, reusing the EC plugin's palette (`#d29922` amber, `#539bf5` blue) for cross-renderer consistency. The `severity-styling` spec already mandates this — no spec delta.

### 6. Harness follow-through in the same change
Remove the two `KNOWN_ISSUES` entries (build then asserts those cases render), delete the `test.fail` markers in `viewport.spec.ts`, `fallback.spec.ts`, `severity.spec.ts`, and `adjacency.spec.ts`, tighten the fallback tolerance to reflect wrapper-relative positioning, and add the static-popup no-overlap invariant. `pages.spec.ts` minimum EC case count goes from 14 back to 16.

## Risks / Trade-offs

- [Static flow layout changes block heights] → verify EC frames/copy-button render correctly via gallery screenshots; the invariant suite catches geometry regressions.
- [Signature wrapping changes popup look on narrow screens] → acceptable: a wrapped signature beats a clipped one; wide viewports are unaffected (max-content up to 560px).
- [Shiki DOM change breaks downstream selectors] → documented; unit tests updated deliberately, not mechanically.
- [Clamped-mode adjacency weakens the invariant] → the clamped branch still requires edge contact + horizontal overlap with the token, so a detached popup cannot pass.

## Migration Plan

Single PR; packages are pre-1.0 alphas so the Shiki DOM change ships as a normal minor note in the changelog. Rollback = revert; fixtures and specs are untouched by rollback.

## Open Questions

- Should the fallback popup prefer below-token (`top: calc(100% + 4px)`) instead of above, to reduce clipping at the top of a `<pre>`? Decide during implementation from gallery screenshots.
