## Why

Shiki's `preprocess` hook is synchronous, but glosharp CLI spawning is async. Users currently face a clunky workflow: they must call `processGloSharpCode()` individually per code block, then create a separate `transformerGloSharpWithResult()` for each — or use `transformerGloSharp()` which can't actually process code (its `preprocess` returns `undefined`). For documentation sites with many C# snippets, this makes the Shiki integration unnecessarily tedious.

## What Changes

- Add `processGloSharpBlocks(codeBlocks, options)` batch processing helper that takes an array of code strings (or `{code, project?, region?}` objects), runs them through glosharp CLI concurrently, and returns a `Map<string, GloSharpResult>` keyed by SHA256 hash of each code string. Blocks without markers are silently skipped.
- Add `transformerGloSharpFromMap(resultMap)` that takes the result map and returns a single Shiki transformer. In `preprocess`, it hashes the incoming code, looks up the result, and returns cleaned code. In `root`, it injects hovers/errors/completions. One transformer instance handles all code blocks.
- Remove the broken `transformerGloSharp()` function which creates a false impression of working async support.
- Export new types: `GloSharpCodeBlock` (input to batch processor) and `GloSharpResultMap` (return type alias).

## Capabilities

### New Capabilities
- `batch-processing`: Batch processing helper (`processGloSharpBlocks`) that concurrently processes multiple code blocks and returns a result map keyed by code hash
- `result-map-transformer`: Map-based Shiki transformer (`transformerGloSharpFromMap`) that looks up pre-computed results by hashing incoming code, enabling a single transformer for all blocks

### Modified Capabilities
- `shiki-transformer`: Remove the broken `transformerGloSharp()` export (**BREAKING**). Retain `transformerGloSharpWithResult()` and `processGloSharpCode()` unchanged.

## Impact

- **Package**: `@glosharp/shiki` (`packages/shiki/`) — source, exports, and tests
- **Breaking**: Removing `transformerGloSharp()` export. Unlikely to affect real users since it couldn't actually process code.
- **Dependencies**: No new dependencies. Uses existing `createGloSharp()` from the `glosharp` bridge package and Node.js `crypto` for SHA256 hashing.
- **No changes** to the glosharp Node bridge, EC plugin, CLI, or C# core.
