# Rendering Feedback Loop

## Why

The only oracle for web rendering quality today is a human running samples and hovering over tokens — automated tests assert on HTML strings (`toContain('glosharp-popup')`) but nothing ever renders the output in a browser, so bugs like the hover tooltip failing to track horizontal scroll (#91) can only be found manually. This change gives agents (and CI) a fast, deterministic way to observe every rendered feature state in a real browser and machine-checkable invariants that encode what "good" looks like.

## What Changes

- Check in `GloSharpResult` JSON fixtures for a curated set of sample `.cs` files, so browser-side rendering tests run in seconds without the .NET SDK or Roslyn; add a drift check that regenerates fixtures from the CLI and fails when they diverge.
- Add a deterministic "rendering gallery" — a kitchen-sink page rendering the full feature matrix (hovers, XML docs, error/warning/info diagnostics, multi-line error spans, completions, highlight/focus/diff, custom tags, long lines forcing horizontal scroll) across both Node render paths (Shiki transformer, Expressive Code plugin) and both themes, with a debug affordance to pin popups open for deterministic capture.
- Add a Playwright test suite asserting geometry and behavior invariants: popup appears adjacent to its token on hover, popup tracks the token during horizontal/vertical scroll, popup stays within the viewport at mobile widths, popup persists while hovered and hides on mouseleave, and the gallery loads with zero console errors — run in both Chromium and Firefox (exercising the CSS anchor-positioning fallback path).
- Wire a `web-rendering` CI job that builds packages and runs the Playwright suite using committed fixtures (no .NET needed).

Out of scope (later phases): screenshot baselines, a11y/keyboard audits, the design rubric + agent screenshot-review pass.

## Capabilities

### New Capabilities

- `render-fixtures`: Committed `GloSharpResult` JSON fixtures for curated samples, a script to regenerate them from the CLI, and a drift check that fails when the CLI output no longer matches committed fixtures.
- `rendering-gallery`: A deterministic browser page that renders every feature state from fixtures across both render paths and themes, with pinnable popup states for capture and inspection.
- `rendering-invariants`: A Playwright test suite asserting geometry/behavior invariants (popup adjacency, scroll tracking, viewport containment, hover lifecycle, console cleanliness) against the gallery in Chromium and Firefox.

### Modified Capabilities

None — the shipped packages (`@glosharp/shiki`, `@glosharp/expressive-code`) are not changed; pinning and instrumentation live entirely in the gallery/test harness.

## Impact

- New workspace under `tests/` (e.g. `tests/rendering/`) containing fixtures, the gallery generator, and Playwright tests; new devDependency on `@playwright/test`.
- `samples/` may gain a few `.cs` files to cover states missing today (long lines, viewport-edge tokens, multi-line errors, custom tags).
- `.github/workflows/ci.yml` gains a `web-rendering` job (Node-only; installs Playwright browsers).
- No changes to published package code or public APIs; existing e2e tests remain as-is.
