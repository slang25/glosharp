## Why

GloSharp currently only compiles against .NET framework reference assemblies. Any snippet that uses a third-party NuGet package (Newtonsoft.Json, EF Core, ASP.NET, etc.) fails to resolve types, producing no hover information. Real-world documentation almost always involves NuGet packages, so this is blocking practical adoption.

## What Changes

- Add a `ProjectAssetsResolver` to GloSharp.Core that reads `project.assets.json` from a restored .csproj and extracts resolved assembly paths for a target framework
- Add `--project` option to the CLI's `process` and `verify` commands, pointing to a .csproj directory (or .csproj file directly)
- Automatically run `dotnet restore` if `project.assets.json` is missing (with a `--no-restore` flag to skip)
- Populate `GloSharpMeta.Packages` with resolved package names and versions
- Add `project` option to the Node.js bridge's `GloSharpProcessOptions`
- Pass `--project` through in Shiki transformer and EC plugin options

## Capabilities

### New Capabilities
- `project-assets-resolution`: Parsing project.assets.json to resolve NuGet package assemblies into Roslyn MetadataReferences for compilation

### Modified Capabilities
- `roslyn-extraction`: Compilation now accepts additional MetadataReferences from resolved NuGet packages alongside framework refs
- `cli-tool`: New `--project` and `--no-restore` options on `process` and `verify` commands
- `node-bridge`: New `project` option on process options, passed as `--project` CLI arg
- `shiki-transformer`: Pass-through `project` option to the bridge
- `expressive-code-plugin`: Pass-through `project` option to the bridge

## Impact

- **GloSharp.Core**: New `ProjectAssetsResolver` class; `GloSharpProcessorOptions` gains `ProjectPath` property; `GloSharpProcessor.Process()` merges project references with framework references
- **GloSharp.Cli**: Argument parsing for `--project` and `--no-restore`; optional `dotnet restore` invocation
- **npm packages**: All three packages gain `project` in their options types
- **Dependencies**: No new NuGet dependencies (project.assets.json is parsed with System.Text.Json which is already available)
- **Backwards compatible**: No breaking changes — standalone mode (no project) remains the default
