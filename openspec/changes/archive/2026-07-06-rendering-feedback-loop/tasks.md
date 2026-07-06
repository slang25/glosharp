# Tasks: Rendering Feedback Loop

## 1. Samples & Fixtures

- [x] 1.1 Audit `samples/` against the feature matrix (hovers, XML docs, error/warning/info, multi-line error span, completions, highlight/focus/diff, custom tags, long lines) and add new `.cs` samples for any missing state
- [x] 1.2 Scaffold `tests/rendering/` npm workspace (package.json, tsconfig, register in root workspaces) with `@playwright/test`, `shiki`, `expressive-code`, and the three `@glosharp/*` packages as deps
- [x] 1.3 Implement `fixtures:update` script: run the GloSharp CLI over the curated sample list, normalize volatile fields (version, absolute paths), write pretty-printed JSON to `tests/rendering/fixtures/`
- [x] 1.4 Implement `fixtures:check` drift script: regenerate to a temp dir, diff against committed fixtures, exit non-zero with a per-file summary on divergence
- [x] 1.5 Run `fixtures:update` and commit the initial fixture set; verify a second run produces a clean git diff (byte-determinism)

## 2. Gallery

- [x] 2.1 Implement Shiki-path gallery rendering: `codeToHtml` + `transformerGloSharpFromMap` over all fixtures, dark and light themes, each case wrapped with `data-gallery-case`
- [x] 2.2 Implement EC-path gallery rendering via the programmatic `ExpressiveCode` engine, embedding its collected baseStyles, theme styles, and JS modules verbatim into the page
- [x] 2.3 Add determinism hygiene: bundle a pinned monospace woff2, add `?static` mode disabling all animations/transitions, ensure stable case ordering
- [x] 2.4 Implement the `?pin=<case-id>` affordance (CSS override for Shiki path, synthetic mouseenter for EC path) as gallery-side script only
- [x] 2.5 Emit the explicit anchor-positioning-fallback variant page (anchor properties stripped) for browser-independent fallback coverage
- [x] 2.6 Add `gallery:build` and `gallery:serve` npm scripts; verify the gallery renders correctly by manual inspection against `examples/expressive-code`

## 3. Playwright Invariants

- [x] 3.1 Configure Playwright: chromium + firefox projects, 1280/768/390 viewport matrix where relevant, `webServer` serving the built gallery, console/pageerror capture failing tests
- [x] 3.2 Create shared geometry helpers and named tolerance constants (`ADJACENCY_TOLERANCE_PX`, relaxed fallback profile)
- [x] 3.3 Implement popup adjacency-on-hover tests for both render paths
- [x] 3.4 Implement scroll-tracking tests (horizontal container scroll on long-line case, vertical page scroll) for the EC path
- [x] 3.5 Implement viewport-containment tests at 390px and 768px, including a token near the right edge
- [x] 3.6 Implement hover lifecycle tests (stays open over popup, hides after leave delay)
- [x] 3.7 Implement simulated `astro:page-load` re-initialization test for the EC path
- [x] 3.8 Implement fallback-variant tests asserting popups remain visible and sanely positioned without anchor positioning
- [x] 3.9 Run the full suite in both browsers locally; eliminate any flakiness (no fixed sleeps)

## 4. CI

- [x] 4.1 Add `web-rendering` job to `.github/workflows/ci.yml`: npm ci, build packages, build gallery, install Playwright browsers, run suite; upload Playwright report/traces as artifacts on failure
- [x] 4.2 Add `fixtures:check` to the existing `node-build-and-test` job (which already has the .NET SDK)
- [x] 4.3 Verify both jobs pass on a PR; confirm the web-rendering job runs without the .NET SDK

## 5. Docs

- [x] 5.1 Add `tests/rendering/README.md` documenting the loop: how to regenerate fixtures, build/serve the gallery, pin cases, run invariants, and how tolerances are governed
- [x] 5.2 Note the gallery + invariant workflow in the root README testing section
