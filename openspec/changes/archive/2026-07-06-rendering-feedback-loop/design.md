# Design: Rendering Feedback Loop

## Context

GloSharp has three render paths (Shiki transformer, Expressive Code plugin, standalone C# HTML renderer); the two Node paths are what documentation sites actually ship. Existing tests assert on HTML strings and never execute the output in a browser, so positioning, scroll-tracking, theming, and interaction bugs (e.g. the horizontal-scroll tooltip bug fixed in #91) are only caught by a human running samples. Every rendering experiment today also pays a 5–30s Roslyn tax per sample via `dotnet run`.

This change builds the phase-1 feedback loop: committed fixtures, a deterministic gallery, and browser-level invariants. Screenshot baselines, a11y audits, and the design-rubric review pass are later phases layered on the same gallery.

## Goals / Non-Goals

**Goals:**
- An agent or CI can verify a rendering change end-to-end in seconds, without a human and without the .NET SDK.
- "Good vs bad" for popup behavior is encoded as geometry/behavior assertions, not screenshots, so it survives restyling.
- The full feature matrix is observable on one servable page for human review too.

**Non-Goals:**
- Screenshot baseline comparison (phase 2 — the gallery is built to make this trivial later).
- Accessibility/keyboard coverage and the taste rubric (phase 3).
- Testing the standalone C# HTML renderer's browser behavior (covered later; its HTML is generated server-side and has no JS).
- Any change to published package behavior.

## Decisions

### 1. Decouple rendering tests from Roslyn via committed fixtures
Rendering tests consume committed `GloSharpResult` JSON (`tests/rendering/fixtures/*.json`); a separate script regenerates them with the CLI and a drift check keeps them honest.
- *Why:* the browser loop becomes pure Node (seconds, no .NET SDK), and semantic changes in the C# core show up as reviewable fixture diffs instead of mysterious rendering-test failures.
- *Alternative considered:* run the CLI live with `--cache-dir`. Rejected — still requires the SDK everywhere, cold runs are slow, and CI caching is another moving part.
- Regeneration normalizes volatile fields (tool version in `meta`, absolute paths) so diffs are always semantic.

### 2. Static gallery generator, not an Astro app
A Node script (`tests/rendering/build-gallery.ts`) renders fixtures to static HTML: the Shiki path via `codeToHtml` + `transformerGloSharpFromMap`, the EC path via the programmatic `ExpressiveCode` engine (rendering blocks, then embedding the engine's collected `baseStyles`, theme styles, and JS modules exactly as the Astro integration would).
- *Why:* no framework build, fast, fully deterministic, trivially servable by Playwright's `webServer`.
- *Alternative considered:* a route in `website/`. Rejected — couples the test loop to the Astro build and the website's own styling, and slows iteration.
- *Risk accepted:* programmatic EC output could drift from what `astro-expressive-code` emits; mitigated by a one-time manual comparison against `examples/expressive-code` and by keeping the embedding logic minimal (use the engine's own outputs verbatim).

### 3. Determinism hygiene up front
Bundled open-license monospace woff2 (e.g. JetBrains Mono), `?static` mode injecting `* { animation: none; transition: none }`, fixed case identifiers (`data-gallery-case="<path>/<fixture>/<theme>"`). This is what makes phase-2 screenshots a small diff instead of a rebuild.

### 4. Pinning lives in the gallery, not the packages
`?pin=<case-id>` gallery script: for the Shiki path it injects CSS that force-displays the case's popups; for the EC path it dispatches the plugin's own `mouseenter` handlers so the shipped positioning logic runs. Published packages stay untouched — the harness observes real behavior rather than a test-mode fork of it.

### 5. Geometry invariants with named tolerances
Adjacency, scroll-tracking, viewport containment, and lifecycle assertions use shared named constants (e.g. `ADJACENCY_TOLERANCE_PX`) in one module, with a relaxed tolerance profile for the no-anchor-positioning fallback. Tolerances are a contract: changing one is a reviewable decision, not a per-test fudge.

### 6. Chromium + Firefox projects; Firefox doubles as the fallback path
Firefox (no CSS Anchor Positioning by default) naturally exercises the Shiki `@supports not` fallback.
- *Risk:* when Firefox ships anchor positioning, fallback coverage silently disappears. Mitigation: the gallery also emits one explicit fallback variant page with anchor-positioning properties stripped, so fallback coverage is browser-independent.

### 7. CI placement
New `web-rendering` job: `npm ci` → build the three packages → build gallery → `playwright install --with-deps chromium firefox` → run suite. Node-only. The fixture drift check runs in the existing `node-build-and-test` job, which already provisions the .NET SDK.

## Risks / Trade-offs

- [Hover timing flakiness] → animations disabled in test mode, Playwright auto-waiting, assertions on settled positions with tolerances; no fixed sleeps.
- [Fixture churn on intentional core changes] → regeneration is one script; drift diffs double as a review artifact for the semantic change.
- [EC programmatic rendering diverges from Astro integration] → keep embedding verbatim from the engine's outputs; spot-check against `examples/expressive-code` during implementation.
- [Two browsers in CI cost ~2–4 min] → acceptable; browsers are cached by setup action; suite itself is seconds because fixtures skip Roslyn.
- [Invariants can't catch aesthetic regressions] → known limit of phase 1; phases 2 (screenshots) and 3 (rubric) sit on top of this gallery.

## Migration Plan

Purely additive: new `tests/rendering/` workspace, new CI job, a few new `samples/*.cs`. No rollback concerns beyond deleting the job/workspace. Existing `tests/e2e` remains unchanged.

## Open Questions

- Should the gallery deploy alongside the website preview (`deploy-website.yml`) for one-click human review? Nice-to-have; defer until the gallery stabilizes.
- Should `tests/e2e`'s sample list converge with the fixture sample list to avoid two curated sets? Lean yes, but out of scope here.
