## 1. File Directive Parser

- [x] 1.1 Create `FileDirectiveParser` that recognizes `#:` lines and extracts directive type, name, and value (package, sdk, property, project)
- [x] 1.2 Implement directive stripping — return cleaned source with `#:` lines removed and a line offset for position mapping
- [x] 1.3 Add `FileDirectiveResult` model containing parsed directives list and cleaned source
- [x] 1.4 Write tests for parsing all directive types (`#:package` with/without version, `#:sdk`, `#:property`, `#:project`)
- [x] 1.5 Write tests for line position mapping after directive stripping

## 2. SDK-Based Package Resolution

- [x] 2.1 Create `FileBasedAppResolver` that invokes `dotnet build <file.cs>` (or `dotnet restore`) to resolve packages via the SDK
- [x] 2.2 Implement SDK version check — parse `dotnet --version` output, fail with clear message if < 10.0
- [x] 2.3 Implement `project.assets.json` discovery from SDK-generated virtual project output
- [x] 2.4 Reuse existing `ProjectAssetsResolver` to read the discovered assets file for `MetadataReference` creation
- [x] 2.5 Write tests for SDK version validation (mock scenarios for .NET 10+ and older SDKs)
- [x] 2.6 Write integration test with a real `#:package` directive resolving a NuGet package (requires .NET 10 SDK)

## 3. TwohashProcessor Integration

- [x] 3.1 Integrate `FileDirectiveParser` into the processing pipeline — run before `MarkerParser`, feed cleaned source forward
- [x] 3.2 Add resolution path selection logic: if `#:` directives present and no project path → use `FileBasedAppResolver`
- [x] 3.3 Wire `FileDirectiveResult` into line mapping so hover/error positions account for stripped `#:` lines
- [x] 3.4 Populate `meta.packages` from parsed `#:package` directives
- [x] 3.5 Add `meta.sdk` field to `TwohashMeta` model, populated from `#:sdk` directive
- [x] 3.6 Write end-to-end test: source with `#:package` + hover query produces correct hover info from the NuGet package's types

## 4. CLI Changes

- [x] 4.1 Add auto-detection logic in CLI process command: check source for `#:` lines when no `--project` is provided
- [x] 4.2 Ensure `--project` flag overrides auto-detection (directives still stripped from output but not used for resolution)
- [x] 4.3 Apply `--no-restore` flag to file-based app resolution path
- [x] 4.4 Add file-based app auto-detection to verify command (per-file detection)
- [x] 4.5 Write CLI integration tests for auto-detection and flag interaction scenarios

## 5. JSON Output Updates

- [x] 5.1 Add `sdk` field to `TwohashMeta` model and JSON serialization
- [x] 5.2 Update Node bridge TypeScript types to include `sdk?: string` on `TwohashMeta`
- [x] 5.3 Write tests verifying `meta.packages` and `meta.sdk` in JSON output for file-based app scenarios
