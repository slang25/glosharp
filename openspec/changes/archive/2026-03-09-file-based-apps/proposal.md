## Why

.NET 10 introduces file-based apps with `#:` directives (`#:package`, `#:sdk`, `#:property`) that let a single `.cs` file declare its own dependencies without a `.csproj`. This is exactly glosharp's lightweight use case — a documentation snippet that's simultaneously compilable and renderable. Supporting these directives eliminates the friction of maintaining a separate project file for simple snippets with NuGet dependencies, and aligns glosharp with the SDK's native approach rather than inventing custom marker syntax.

## What Changes

- Parse `#:package`, `#:sdk`, `#:property`, and `#:project` directives from source files
- Strip `#:` directive lines from rendered output (they're build metadata, not displayable code)
- Resolve NuGet packages via the .NET SDK's file-based app support (run `dotnet build <file.cs>`, read generated `project.assets.json`)
- Handle SDK switching (`#:sdk Microsoft.NET.Sdk.Web` adds ASP.NET framework references)
- Auto-detect file-based app mode when source contains `#:` directives and no `--project` flag is provided
- Populate `meta.packages` and `meta.sdk` from parsed directives in JSON output

## Capabilities

### New Capabilities
- `file-based-app-directives`: Parsing, stripping, and resolution of `#:` directives (`#:package`, `#:sdk`, `#:property`, `#:project`) for file-based app compilation

### Modified Capabilities
- `marker-parsing`: MarkerParser must recognize and strip `#:` directive lines from processed output, maintaining correct line mappings
- `cli-tool`: CLI auto-detects file-based app mode and invokes SDK-based resolution when `#:` directives are present
- `json-output`: `meta` object gains `sdk` field; `meta.packages` populated from `#:package` directives

## Impact

- **Core**: New `FileBasedAppResolver` alongside existing `ProjectAssetsResolver` and `FrameworkResolver`; `GloSharpProcessor` gains a third resolution path
- **MarkerParser**: Extended to recognize `#:` lines as directive markers for stripping
- **CLI**: New auto-detection logic in the process command; no new flags required (detection is automatic)
- **JSON output**: Additive change to `meta` — new optional `sdk` field
- **Dependencies**: Requires .NET 10 SDK on the build machine when `#:` directives are used
- **Node bridge**: No type changes needed — detection is source-content-driven
