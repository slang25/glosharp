## Why

The MVP supports hover queries (`^?`) and error diagnostics, but two key features from the original design remain unimplemented: completion queries (`^|` markers) and region extraction (`--region` flag). Completions let documentation authors show IntelliSense-style suggestion lists at specific positions — a powerful way to demonstrate API discoverability. Region extraction lets authors maintain compilable sample projects with `#region` markers and extract specific snippets for docs, keeping code DRY and always verified against the compiler.

## What Changes

- Add `^|` completion marker parsing to `MarkerParser`, recording the query position
- Add Roslyn completion extraction using `CompletionService.GetCompletionsAsync()` via an `AdhocWorkspace`
- Populate the `completions` array in JSON output (currently always empty)
- Add `--region` CLI option to extract a named `#region` from a source file while compiling the full file
- Update the Node.js bridge to pass `region` option to the CLI
- Update Shiki transformer and EC plugin to render completion lists
- Add sample files and tests for both features

## Capabilities

### New Capabilities

- `completion-extraction`: Parsing `^|` markers, querying Roslyn's CompletionService, and returning structured completion items in JSON output
- `region-extraction`: Extracting named `#region` blocks from source files, compiling the full file for type accuracy, and outputting only the region's code with correct position mapping

### Modified Capabilities

- `marker-parsing`: Adding `^|` completion marker recognition and column position calculation
- `cli-tool`: Adding `--region` option to `process` and `verify` commands
- `json-output`: Populating the `completions` array with structured completion items
- `node-bridge`: Adding `region` option to process call and CLI argument construction
- `shiki-transformer`: Rendering completion list annotations in HAST output
- `expressive-code-plugin`: Rendering completion list annotations via EC custom annotation class

## Impact

- **TwoHash.Core**: New `CompletionExtractor` class using `AdhocWorkspace` + `CompletionService`; new `RegionExtractor` class for `#region` parsing; updates to `MarkerParser` and `TwohashProcessor`
- **TwoHash.Cli**: New `--region` argument on `process` and `verify` commands
- **Node.js packages**: Updated types, option passing, and rendering for completions and regions
- **Dependencies**: `Microsoft.CodeAnalysis.CSharp.Features` is already referenced (needed for `CompletionService`)
- **Tests**: New test fixtures for completion and region scenarios
