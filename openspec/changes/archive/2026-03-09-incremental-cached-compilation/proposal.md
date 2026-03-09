## Why

Documentation sites with many C# snippets sharing the same project context recompile from scratch on every `twohash process` call. Reference resolution (finding framework DLLs, parsing project.assets.json, running `dotnet build` for file-based apps) and Roslyn compilation are repeated identically for each snippet. A site with 50 snippets from the same project does 50× the work. Adding caching at two levels — in-process compilation context reuse and disk-based result caching — eliminates redundant work within a build and across rebuilds.

## What Changes

- **New disk-based result cache**: A `--cache-dir <path>` CLI option that caches full `TwohashResult` JSON to disk, keyed by a hash of (source code, target framework, project context, twohash version). Cache hits skip all Roslyn work entirely.
- **In-process compilation context reuse**: When processing multiple snippets in the same CLI invocation (e.g., `twohash verify`), resolved `MetadataReference[]` arrays are cached by compilation context key, avoiding redundant reference resolution.
- **Node bridge passthrough**: `cacheDir` option added to the Node bridge, passed through to the CLI as `--cache-dir`.

## Capabilities

### New Capabilities
- `result-cache`: Disk-based caching of TwohashResult JSON, keyed by source and compilation context. Handles cache key computation, file I/O, version-based invalidation, and cache directory management.
- `compilation-context-cache`: In-process caching of resolved MetadataReference arrays by compilation context (framework, packages, project). Used within multi-file CLI invocations like `verify`.

### Modified Capabilities
- `cli-tool`: Adding `--cache-dir <path>` option to `process` and `verify` commands.
- `node-bridge`: Adding `cacheDir` option to `TwohashOptions` and `TwohashProcessOptions`, passed through to CLI.

## Impact

- **Core library** (`src/TwoHash.Core/`): New cache classes, `TwohashProcessorOptions` gains `CacheDir` property, `TwohashProcessor` gains optional cache dependencies.
- **CLI** (`src/TwoHash.Cli/`): New `--cache-dir` option on both commands, `verify` command uses compilation context cache internally.
- **Node bridge** (`packages/twohash/`): New `cacheDir` option in types and CLI argument builder.
- **No breaking changes**: All caching is opt-in. Existing behavior unchanged when `--cache-dir` is not specified.
- **No new dependencies**: Uses standard library SHA256 and file I/O.
