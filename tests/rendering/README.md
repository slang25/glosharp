# Rendering feedback loop

Browser-level verification of GloSharp's web rendering (Shiki transformer and
Expressive Code plugin). Everything here runs from **committed fixtures** —
no .NET SDK, no Roslyn — so the loop is seconds, not minutes, and works for
agents and CI alike.

## The loop

```
samples/*.cs ──(fixtures:update, needs .NET)──▶ fixtures/*.json   (committed)
fixtures/*.json ──(gallery:build)──▶ gallery-dist/*.html          (generated)
gallery-dist ──(npm test)──▶ Playwright invariants, Chromium + Firefox
```

| Command | What it does |
| --- | --- |
| `npm run fixtures:update` | Regenerate `fixtures/*.json` by running the GloSharp CLI over every `samples/*.cs` (needs the .NET SDK; pinned to `net8.0`). Commit the result. |
| `npm run fixtures:check` | Fail if the CLI's output no longer matches the committed fixtures (runs in the `node-build-and-test` CI job). |
| `npm run gallery:build` | Render every fixture through both render paths × dark/light themes into `gallery-dist/` (requires `npm run build -w @glosharp/core -w @glosharp/shiki -w @glosharp/expressive-code`). |
| `npm run gallery:serve` | Serve the gallery at `http://localhost:4173/` for human review. |
| `npm test` | Run the Playwright invariant suite (builds + serves the gallery itself via `webServer`). |

## The gallery

Six deterministic pages (`shiki-{dark,light}`, `ec-{dark,light}`,
`shiki-fallback`, `index`). Each rendered fixture is wrapped in
`<section data-gallery-case="<path>/<fixture>/<theme>">`.

Debug affordances (gallery-side only; the published packages contain no
test-specific code):

- `?static` — disables all animations/transitions for deterministic capture.
- `?pin=<case-id>&token=<n>` — forces the n-th hover token's popup open
  without a pointer (drives the EC plugin's own `mouseenter` logic; overrides
  the `:hover` CSS for the Shiki path).
- `shiki-fallback.html` — the Shiki page with all CSS anchor-positioning
  wiring stripped, simulating a browser without anchor support regardless of
  the running browser.

Determinism: a pinned JetBrains Mono woff2 is bundled (no system fonts), case
order is fixed, and the EC plugin gets its data from `scripts/glosharp-stub.mjs`,
a fake `glosharp` executable that serves fixture JSON by matching stdin.

## The invariants

`specs/*.spec.ts`, run in Chromium and Firefox. What "good" means, as
assertions:

- **Adjacency** — hovering a token opens its popup next to that token.
- **Scroll tracking** — an open EC popup follows its token through container
  and page scrolls, and hides when the token leaves the container (the
  regression class fixed in PR #91).
- **Viewport containment** — opened popups stay inside 390px/768px viewports.
- **Lifecycle** — popups survive the pointer moving onto them and close after
  it leaves.
- **Re-init** — EC popups still work after a simulated Astro view transition.
- **Severity styling** — warnings render in a different color than errors.
- **Console cleanliness** — every test fails on any console error or uncaught
  exception (shared fixture in `specs/helpers.ts`).

### Tolerances

All geometry thresholds are named constants in `specs/helpers.ts`
(`ADJACENCY_TOLERANCE_PX`, `EC_VERTICAL_GAP_RANGE_PX`, …). They are a
contract: loosening one is a reviewable decision, never a per-test fudge.

### Known findings (`test.fail` / `KNOWN_ISSUES`)

Assertions that document real, currently-unfixed defects are marked
`test.fail(…, reason)` in the specs. They keep CI green while the defect
exists and flag loudly ("passed unexpectedly") the moment it is fixed — at
which point remove the marker. Fixtures that crash a render path outright are
skipped via `KNOWN_ISSUES` in `scripts/build-gallery.ts`, with each skip
logged at build time so the coverage gap is never silent.

Both lists are currently empty — the five defects found by the loop's first
run were fixed in the `fix-rendering-defects` change. Two `it.fails` markers
remain in the package unit tests for open product decisions (whether expected
`@errors:` diagnostics should render, and per-block `region` overrides being
incompatible with `--stdin`).

## Adding coverage

1. Add a `.cs` sample under `samples/` exercising the state.
2. `npm run fixtures:update` and commit the new fixture.
3. The gallery picks it up automatically; add targeted assertions under
   `specs/` if the state has behavior worth pinning down.
