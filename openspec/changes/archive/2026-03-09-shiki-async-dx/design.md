## Context

The `@glosharp/shiki` package provides a Shiki transformer that injects glosharp type information (hovers, errors, completions) into syntax-highlighted code. The core challenge is that Shiki's `preprocess` hook is synchronous, but glosharp processing requires spawning an async CLI subprocess.

Currently the package exports three functions:
- `transformerGloSharpWithResult(result)` — works but requires pre-computing a result per block
- `processGloSharpCode(code, options)` — async helper that processes a single block
- `transformerGloSharp(options)` — broken; its `preprocess` returns `undefined` because it can't run async

For a documentation site with 50+ C# snippets, users must loop through blocks individually, call `processGloSharpCode` for each, then create a new transformer per block. This is the DX gap.

## Goals / Non-Goals

**Goals:**
- Provide a single async call to batch-process all code blocks at once
- Provide a single transformer instance that works across all code blocks via result lookup
- Remove the misleading `transformerGloSharp()` that can't work
- Keep the existing `transformerGloSharpWithResult()` and `processGloSharpCode()` for single-block use cases

**Non-Goals:**
- Concurrency limiting / backpressure — use simple `Promise.all` for now; can add later if CLI spawning becomes a bottleneck
- Streaming / incremental processing — all blocks are processed before any rendering begins
- Changes to the Node bridge (`glosharp` package) or Expressive Code plugin
- Changes to the CLI or C# core

## Decisions

### 1. Hash key: SHA256 of raw code (with markers)

The result map is keyed by SHA256 hash of the original code string (before marker removal). This matches the existing caching strategy in `createGloSharp()` (`glosharp.ts` line 22-23). When `transformerGloSharpFromMap` receives code in `preprocess`, it hashes the incoming code to look up the result — this works because Shiki passes the original code to `preprocess` before any transformation.

**Alternative**: Use the code string directly as key. Rejected because long code strings make poor map keys and SHA256 is already used in the bridge's cache.

### 2. Batch function accepts both string[] and object[]

`processGloSharpBlocks` accepts `Array<string | GloSharpCodeBlock>` where `GloSharpCodeBlock = { code: string; project?: string; region?: string }`. String entries use the shared options; object entries can override `project` and `region` per block. This covers both the common case (all blocks share one project) and the edge case (mixed projects).

**Alternative**: Only accept strings with shared options. Rejected because some sites mix snippets from different projects.

### 3. Remove `transformerGloSharp()` (breaking)

The current `transformerGloSharp()` creates a false API surface — it looks like it works but its `preprocess` returns `undefined`, meaning code markers are never stripped and results are never injected. Removing it avoids confusion. The replacement is the clearer two-step pattern: `processGloSharpBlocks` → `transformerGloSharpFromMap`.

**Alternative**: Fix it by making it work with the map pattern internally. Rejected because the old API signature doesn't communicate the async requirement — better to have an explicit two-step API.

### 4. Reuse existing HAST utilities

`transformerGloSharpFromMap` reuses the same `injectHovers`, `injectErrors`, `injectCompletions`, and `findCodeLines` functions already in `transformer.ts`. No new rendering logic needed.

### 5. Graceful miss on map lookup

If `transformerGloSharpFromMap` receives code that isn't in the result map (either because it had no markers, or wasn't included in the batch), it does nothing — `preprocess` returns `undefined` (no code replacement) and `root` skips injection. This lets the transformer be registered globally without breaking non-glosharp code blocks.

## Risks / Trade-offs

- **[Unbounded concurrency]** → `Promise.all` spawns all CLI processes at once. For sites with 100+ snippets this could be problematic. Mitigation: the `createGloSharp()` bridge already caches by code hash, so duplicate blocks are free. Future work can add a `concurrency` option if needed.
- **[Breaking change]** → Removing `transformerGloSharp()`. Mitigation: it never worked correctly, so real-world impact is minimal. Document in changelog.
- **[Hash collision]** → SHA256 collision is astronomically unlikely for code strings. No mitigation needed.
