## 1. Project Assets Resolver

- [ ] 1.1 Create `ProjectAssetsResolver` class in TwoHash.Core that accepts a project path (file or directory) and locates `obj/project.assets.json`
- [ ] 1.2 Parse the `packageFolders` section to determine the NuGet global packages directory
- [ ] 1.3 Parse the `targets` section to select the appropriate TFM target (explicit or first available)
- [ ] 1.4 Extract resolved assembly paths from `compile` entries (falling back to `runtime`), skipping `_._` placeholders
- [ ] 1.5 Build `MetadataReference` list from resolved assembly paths
- [ ] 1.6 Extract package name/version metadata for `TwohashMeta.Packages`
- [ ] 1.7 Write unit tests for assets parsing: single package, transitive deps, missing compile assets, custom package folder, TFM selection, TFM not found

## 2. Core Integration

- [ ] 2.1 Add `ProjectPath` property to `TwohashProcessorOptions`
- [ ] 2.2 Update `TwohashProcessor.Process()` to merge project references with framework references when `ProjectPath` is set
- [ ] 2.3 Populate `TwohashMeta.Packages` from resolved project metadata
- [ ] 2.4 Infer target framework from project assets when not explicitly specified
- [ ] 2.5 Write integration test: compile snippet using Newtonsoft.Json with project context, verify hover resolves `JsonConvert`

## 3. CLI Changes

- [ ] 3.1 Add `--project` argument parsing to `process` command
- [ ] 3.2 Add `--project` argument parsing to `verify` command
- [ ] 3.3 Add `--no-restore` flag parsing
- [ ] 3.4 Implement auto-restore: run `dotnet restore` when assets file missing and `--no-restore` not set
- [ ] 3.5 Update help text with new options
- [ ] 3.6 Write CLI integration tests: process with project, verify with project, auto-restore trigger, no-restore skip

## 4. Node.js Bridge

- [ ] 4.1 Add `project` and `noRestore` to `TwohashProcessOptions` type
- [ ] 4.2 Pass `--project` and `--no-restore` arguments when spawning CLI
- [ ] 4.3 Write unit tests for new CLI argument construction

## 5. Shiki Transformer

- [ ] 5.1 Add `project` option to `transformerTwohash()` options type
- [ ] 5.2 Pass `project` through to bridge `process()` call
- [ ] 5.3 Write test verifying project option is forwarded

## 6. Expressive Code Plugin

- [ ] 6.1 Add `project` option to `pluginTwohash()` options type
- [ ] 6.2 Pass `project` through to bridge `process()` call
- [ ] 6.3 Write test verifying project option is forwarded
