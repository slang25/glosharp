## Why

Shiki's `preprocess` hook is synchronous, but twohash CLI spawning is async. Users currently face a clunky workflow: they must call `processTwohashCode()` individually per code block, then create a separate `transformerTwohashWithResult()` for each — or use `transformerTwohash()` which can't actually process code (its `preprocess` returns `undefined`). For documentation sites with many C# snippets, this makes the Shiki integration unnecessarily tedious.

## What Changes

- Add `processTwohashBlocks(codeBlocks, options)` batch processing helper that takes an array of code strings (or `{code, project?, region?}` objects), runs them through twohash CLI concurrently, and returns a `Map<string, TwohashResult>` keyed by SHA256 hash of each code string. Blocks without markers are silently skipped.
- Add `transformerTwohashFromMap(resultMap)` that takes the result map and returns a single Shiki transformer. In `preprocess`, it hashes the incoming code, looks up the result, and returns cleaned code. In `root`, it injects hovers/errors/completions. One transformer instance handles all code blocks.
- Remove the broken `transformerTwohash()` function which creates a false impression of working async support.
- Export new types: `TwohashCodeBlock` (input to batch processor) and `TwohashResultMap` (return type alias).

## Capabilities

### New Capabilities
- `batch-processing`: Batch processing helper (`processTwohashBlocks`) that concurrently processes multiple code blocks and returns a result map keyed by code hash
- `result-map-transformer`: Map-based Shiki transformer (`transformerTwohashFromMap`) that looks up pre-computed results by hashing incoming code, enabling a single transformer for all blocks

### Modified Capabilities
- `shiki-transformer`: Remove the broken `transformerTwohash()` export (**BREAKING**). Retain `transformerTwohashWithResult()` and `processTwohashCode()` unchanged.

## Impact

- **Package**: `@twohash/shiki` (`packages/shiki/`) — source, exports, and tests
- **Breaking**: Removing `transformerTwohash()` export. Unlikely to affect real users since it couldn't actually process code.
- **Dependencies**: No new dependencies. Uses existing `createTwohash()` from the `twohash` bridge package and Node.js `crypto` for SHA256 hashing.
- **No changes** to the twohash Node bridge, EC plugin, CLI, or C# core.
