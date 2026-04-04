## Why

Documentation sites with many C# snippets sharing the same project context recompile from scratch on every `glosharp process` call. Reference resolution (finding framework DLLs, parsing project.assets.json, running `dotnet build` for file-based apps) and Roslyn compilation are repeated identically for each snippet. A site with 50 snippets from the same project does 50× the work. Adding caching at two levels — in-process compilation context reuse and disk-based result caching — eliminates redundant work within a build and across rebuilds.

## What Changes

- **New disk-based result cache**: A `--cache-dir <path>` CLI option that caches full `GloSharpResult` JSON to disk, keyed by a hash of (source code, target framework, project context, glosharp version). Cache hits skip all Roslyn work entirely.
- **In-process compilation context reuse**: When processing multiple snippets in the same CLI invocation (e.g., `glosharp verify`), resolved `MetadataReference[]` arrays are cached by compilation context key, avoiding redundant reference resolution.
- **Node bridge passthrough**: `cacheDir` option added to the Node bridge, passed through to the CLI as `--cache-dir`.

## Capabilities

### New Capabilities
- `result-cache`: Disk-based caching of GloSharpResult JSON, keyed by source and compilation context. Handles cache key computation, file I/O, version-based invalidation, and cache directory management.
- `compilation-context-cache`: In-process caching of resolved MetadataReference arrays by compilation context (framework, packages, project). Used within multi-file CLI invocations like `verify`.

### Modified Capabilities
- `cli-tool`: Adding `--cache-dir <path>` option to `process` and `verify` commands.
- `node-bridge`: Adding `cacheDir` option to `GloSharpOptions` and `GloSharpProcessOptions`, passed through to CLI.

## Impact

- **Core library** (`src/GloSharp.Core/`): New cache classes, `GloSharpProcessorOptions` gains `CacheDir` property, `GloSharpProcessor` gains optional cache dependencies.
- **CLI** (`src/GloSharp.Cli/`): New `--cache-dir` option on both commands, `verify` command uses compilation context cache internally.
- **Node bridge** (`packages/glosharp/`): New `cacheDir` option in types and CLI argument builder.
- **No breaking changes**: All caching is opt-in. Existing behavior unchanged when `--cache-dir` is not specified.
- **No new dependencies**: Uses standard library SHA256 and file I/O.
