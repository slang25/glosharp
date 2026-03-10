## 1. Dependencies & Project Setup

- [x] 1.1 Add `Basic.CompilerLog.Util` NuGet package reference to `TwoHash.Core.csproj`
- [x] 1.2 Verify the package restores and the project builds cleanly

## 2. ComplogResolver

- [x] 2.1 Create `ComplogResolver` class in `TwoHash.Core` that opens a complog file, selects a compilation (by project name or first C# compilation), and extracts `MetadataReference[]`, `CSharpCompilationOptions`, `CSharpParseOptions`, target framework, and package list
- [x] 2.2 Implement `IDisposable` on `ComplogResolver` to release complog reader file handles
- [x] 2.3 Implement project selection: default to first C# compilation, error with available names if specified project not found, error if no C# compilations exist
- [x] 2.4 Implement package extraction from complog metadata references (assembly name + version)

## 3. TwohashProcessor Integration

- [x] 3.1 Add `ComplogPath` and `ComplogProject` properties to `TwohashProcessorOptions`
- [x] 3.2 Add complog resolution path in `TwohashProcessor.ProcessAsync()` that bypasses FrameworkResolver/ProjectAssetsResolver/FileBasedAppResolver when complog is specified
- [x] 3.3 Integrate complog-sourced references with `CompilationContextCache` (key: complog path + project name + last-write-time)
- [x] 3.4 Integrate complog with result cache (key includes complog path instead of project/framework/packages)
- [x] 3.5 Populate `meta.complog`, `meta.targetFramework`, and `meta.packages` from complog data

## 4. Models & JSON Output

- [x] 4.1 Add `Complog` property (string, nullable) to `TwohashMeta` model
- [x] 4.2 Verify JSON serialization includes `meta.complog` when set and omits when null

## 5. CLI Integration

- [x] 5.1 Add `--complog <path>` option to `process`, `verify`, and `render` commands
- [x] 5.2 Add `--complog-project <name>` option to `process`, `verify`, and `render` commands
- [x] 5.3 Add validation: `--complog` and `--project` are mutually exclusive (error if both specified)
- [x] 5.4 Add validation: `--complog-project` requires `--complog` (error if used alone)
- [x] 5.5 Wire complog options through config loading (merge config defaults with CLI args)

## 6. Config File

- [x] 6.1 Add `complog` and `complogProject` properties to `TwohashConfig` model in `ConfigLoader`
- [x] 6.2 Resolve `complog` path relative to config file location (same as `project` and `cacheDir`)
- [x] 6.3 Update `init` command scaffold to include commented-out `complog` field

## 7. Node Bridge

- [x] 7.1 Add `complog` and `complogProject` optional properties to `TwohashOptions` interface
- [x] 7.2 Add `complog` and `complogProject` optional properties to `TwohashProcessOptions` interface
- [x] 7.3 Pass `--complog` and `--complog-project` args to CLI when set in bridge `process()` method

## 8. Tests

- [x] 8.1 Write tests for `ComplogResolver`: open valid complog, project selection, error cases (not found, invalid, no C# compilations)
- [x] 8.2 Write tests for `TwohashProcessor` with complog: verify references from complog are used, verify meta fields populated, verify mutual exclusivity with project
- [x] 8.3 Write tests for CLI: `--complog` and `--complog-project` option parsing, mutual exclusivity validation
- [x] 8.4 Write tests for config loading: `complog` path resolution relative to config file
