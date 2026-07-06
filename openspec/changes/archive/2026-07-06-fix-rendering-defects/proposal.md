# Fix Rendering Defects

## Why

The rendering feedback loop (change `rendering-feedback-loop`, PR #95) surfaced five real rendering defects on its first run — two of them crash the Expressive Code render outright, and all five degrade documentation output that users actually see. Each defect is already pinned by a `test.fail` marker or a logged `KNOWN_ISSUES` gallery skip, so fixes are verified simply by removing the marker and watching the invariant pass.

## What Changes

1. **EC completion lists crash the render** — `GloSharpCompletionAnnotation` uses a zero-width inline range but returns a `<ul>`; EC core rejects render output whose node count differs from its input. Fix: annotate the whole line and nest the completion list inside a same-count wrapper.
2. **EC multi-line error messages crash the render** — `GloSharpErrorAnnotation` with `messageOnly` returns `nodesToTransform` plus a message box (n+1 nodes). Fix: nest the message box inside a wrapper of the line's nodes, preserving count.
3. **EC static `^?` popups overlap content** — *invalidated during implementation*: geometry measurement showed static containers already render in normal flow with zero overlap; the screenshot that motivated this finding was taken mid-hover and the covering box was the ephemeral hover popup (expected behavior). Retained: a no-overlap invariant as a regression guard, since the behavior is now spec-required.
4. **EC hover popups overflow small viewports** — `white-space: nowrap`, `width: max-content`, no max-width, and no horizontal clamping in `positionPopup()`. Fix: viewport-aware max-width plus horizontal clamping in the positioning JS.
5. **Shiki popups/severities degrade badly** — `@glosharp/shiki`'s `style.css` (a) has no severity-specific colors, so warnings render red like errors (violating the existing `severity-styling` spec — pure compliance fix, no spec delta), and (b) has no positioning fallback when CSS Anchor Positioning is unavailable, leaving popups thousands of pixels from their token. Fix: add severity color rules, and move the popup inside the hover wrapper so an `@supports not` block can position it relative to the token (which also lets the popup stay open when the pointer moves onto it, without racing `display: none`).

Harness follow-through: remove the corresponding `test.fail` markers in `tests/rendering/specs/`, the `KNOWN_ISSUES` skips in the gallery build, and regenerate nothing (fixtures are unaffected — these are render-side fixes).

## Capabilities

### New Capabilities

None.

### Modified Capabilities

- `expressive-code-plugin`: completion and multi-line-error annotations must produce EC-core-valid render output (same node count); static popups must participate in layout (no overlap); popups must be clamped to the viewport.
- `shiki-transformer`: the popup element moves from sibling to child of the hover wrapper; a functional `@supports not` positioning fallback is required when CSS Anchor Positioning is unavailable.

## Impact

- `packages/expressive-code/src/plugin.ts` (annotation render functions, static popup CSS, popup clamping CSS + `positionPopup()`), `packages/shiki/src/transformer.ts` (popup nesting) and `packages/shiki/src/style.css` (severity colors, child-popup selectors, fallback block).
- Package unit tests updated where they assert the old DOM shape; `tests/rendering` markers/skips removed; the previously skipped `ec/completions` and `ec/multi-line-error` gallery cases start rendering.
- No CLI/C# core, JSON format, or fixture changes. DOM structure change in the Shiki output (popup nesting) is visible to consumers who styled `.glosharp-hover + .glosharp-popup` directly — noted in design, not **BREAKING** for documented usage since `@glosharp/shiki/style.css` is the supported styling entry point.
