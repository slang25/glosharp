## Context

GloSharp currently supports two compilation modes: framework-only (standalone `.cs` with no NuGet packages) and project-based (`.csproj` with `project.assets.json`). The gap is snippets that need NuGet packages but don't warrant a full project file.

.NET 10 (shipping late 2025) introduces file-based apps — single `.cs` files with `#:` directives that the SDK resolves natively. This is the SDK's own solution to the same problem. Decision 003 already selected file-based apps as the default path for .NET 10+.

The current `MarkerParser` handles `//` comment-based markers. `#:` directives are syntactically different — they're not comments, they're preprocessor-level directives handled by the SDK before the compiler sees them.

## Goals / Non-Goals

**Goals:**
- Parse `#:package`, `#:sdk`, `#:property`, and `#:project` directives from source
- Strip `#:` lines from rendered output
- Resolve NuGet packages by delegating to the .NET SDK (`dotnet build <file.cs>`)
- Auto-detect file-based app mode from source content
- Handle additional framework packs when `#:sdk` specifies a non-default SDK

**Non-Goals:**
- Custom NuGet resolution logic (we delegate to the SDK entirely)
- Supporting `#:` directives on pre-.NET 10 SDKs
- complog support (separate roadmap item)
- Caching/incremental compilation across invocations

## Decisions

### 1. Directive parsing: separate parser, not MarkerParser extension

**Decision**: Create a `FileDirectiveParser` that runs before `MarkerParser` to extract and strip `#:` lines.

**Alternatives considered**:
- Extend `MarkerParser` to handle `#:` lines alongside `//` markers
- Parse directives in `GloSharpProcessor` directly

**Rationale**: `#:` directives are structurally different from comment markers — they appear at the top of the file, use a different prefix, and carry different semantics (build metadata vs. display instructions). A separate parser keeps concerns clean and is easier to test independently. It runs first, producing a `FileDirectiveResult` with extracted directives and cleaned source, which then flows into the existing `MarkerParser`.

### 2. Package resolution: delegate to `dotnet build`

**Decision**: When `#:` directives are detected, run `dotnet build <file.cs> --getProperty:ProjectAssetsFile --getProperty:TargetFramework` to get the assets file path, then reuse the existing `ProjectAssetsResolver` to read it.

**Alternatives considered**:
- **(A) Run `dotnet build <file.cs>` fully, read virtual project output**: Heavier than needed — we don't need to compile, just resolve.
- **(B) Generate a temp `.csproj` from directives, run `dotnet restore`**: Works but duplicates what the SDK already does.
- **(C) Use `dotnet build <file.cs>` with design-time targets**: The SDK generates a virtual project under a temp directory. We can use `--getProperty:ProjectAssetsFile` to find where it wrote `project.assets.json`.

**Rationale**: Option C gives us the resolved `project.assets.json` path without a full compilation. The .NET SDK handles all the complexity of `#:` directive interpretation, NuGet resolution, and framework pack selection. We reuse our existing `ProjectAssetsResolver` for the actual DLL path extraction. If `--getProperty` isn't available for file-based apps, we fall back to running `dotnet restore <file.cs>` and finding the assets file in the generated obj directory.

### 3. SDK detection: check `dotnet --version`

**Decision**: Before attempting file-based app resolution, verify the SDK is >= 10.0 by parsing `dotnet --version`. If an older SDK is installed, fail with a clear error message suggesting either upgrading the SDK or using `--project`.

**Rationale**: `#:` directives are a .NET 10 feature. Attempting to pass them to an older SDK will produce confusing error messages. Better to fail early with guidance.

### 4. Framework pack resolution for `#:sdk`

**Decision**: When `#:sdk Microsoft.NET.Sdk.Web` is present, the SDK's restore will include `Microsoft.AspNetCore.App.Ref` assemblies in `project.assets.json` automatically. No special handling needed in glosharp — the `ProjectAssetsResolver` already reads whatever the SDK resolves.

**Rationale**: The SDK handles framework pack selection based on the SDK type. We don't need to duplicate that logic.

### 5. `#:` line stripping: happens in FileDirectiveParser

**Decision**: `FileDirectiveParser` strips `#:` lines and adjusts line numbers before source reaches `MarkerParser`. The original source (with `#:` lines) is preserved in `GloSharpResult.Original`.

**Rationale**: `#:` lines must be excluded from both rendered output and Roslyn compilation (Roslyn doesn't understand them). Stripping early simplifies downstream processing. The existing line-mapping infrastructure in `MarkerParser` then handles the remaining `//` markers.

### 6. Auto-detection in CLI

**Decision**: When no `--project` flag is provided, the CLI checks if the source contains any `#:` lines. If yes, it uses file-based app resolution. If no, it falls back to framework-only mode (existing behavior).

**Rationale**: This is zero-friction — users don't need a new flag. The presence of `#:` directives is an unambiguous signal that file-based app resolution is needed.

## Risks / Trade-offs

- **[.NET 10 SDK required]** → Users without .NET 10 SDK cannot use `#:` directives. Mitigation: clear error message with upgrade instructions; `.csproj` path remains available.
- **[SDK invocation latency]** → Running `dotnet build/restore` adds seconds to processing. Mitigation: acceptable for build-time tool; caching is a separate roadmap item.
- **[Virtual project location]** → The SDK generates virtual project artifacts in a temp/obj directory. The exact location may vary across SDK versions. Mitigation: use `--getProperty:ProjectAssetsFile` to discover the path rather than guessing.
- **[`#:` syntax may evolve]** → File-based apps are new in .NET 10 and the directive syntax could change. Mitigation: we parse minimally (just to detect and strip); the SDK handles interpretation.
- **[Roslyn version compatibility]** → The `#:` directives are not valid C# syntax — Roslyn will produce parse errors on them. Mitigation: `FileDirectiveParser` strips them before Roslyn sees the source.
