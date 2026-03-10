## Why

Documentation builds often run on different machines from code builds — separate CI jobs, pure Node.js doc builders, or contributor machines without the full .NET SDK + NuGet cache. Today, twohash requires either a restored .csproj or a .NET 10 SDK for file-based apps. complog (Roslyn's portable compilation format by Jared Parsons) lets a CI step capture the full compilation state into a single file that twohash can consume anywhere, with no SDK or NuGet resolution needed.

## What Changes

- New `--complog <path>` CLI option on `process`, `verify`, and `render` commands to accept a `.complog` file as the compilation source
- New `ComplogResolver` class that opens a complog file using `Basic.CompilerLog.Util`, selects the appropriate compilation, and extracts references + options for use by `TwohashProcessor`
- New `--complog-project <name>` CLI option to select a specific project from multi-project complogs (defaults to the first/only compilation)
- `TwohashProcessor` gains a complog resolution path that bypasses all existing resolvers (FrameworkResolver, ProjectAssetsResolver, FileBasedAppResolver) when a complog is provided
- Node bridge types updated with `complog` and `complogProject` options
- Config file schema extended with `complog` and `complogProject` fields
- New NuGet dependency: `Basic.CompilerLog.Util` added to TwoHash.Core

## Capabilities

### New Capabilities
- `complog-resolution`: Opening complog files, selecting compilations by project name, extracting references and compiler options for use in TwohashProcessor

### Modified Capabilities
- `cli-tool`: Adding `--complog` and `--complog-project` options to `process`, `verify`, and `render` commands
- `node-bridge`: Adding `complog` and `complogProject` options to `TwohashOptions` and `TwohashProcessOptions`
- `config-file-loading`: Adding `complog` and `complogProject` fields to `twohash.config.json` schema
- `json-output`: Adding `meta.complog` field to indicate complog was used as compilation source

## Impact

- **Dependencies**: New NuGet package `Basic.CompilerLog.Util` added to TwoHash.Core
- **CLI**: New options on existing commands; `--complog` is mutually exclusive with `--project` and file-based app directives
- **Node bridge**: New optional fields in TypeScript interfaces (non-breaking)
- **Config file**: New optional fields (non-breaking)
- **Caching**: Result cache key must include complog path/hash; compilation context cache can store complog-sourced references
