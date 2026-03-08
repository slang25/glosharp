## Context

Twohash's `TwohashProcessor` currently creates a Roslyn `CSharpCompilation` using only framework reference assemblies resolved by `FrameworkResolver`. The `TwohashMeta.Packages` field is always empty. To support real-world documentation snippets that use NuGet packages, we need to resolve package assemblies from a .csproj's `project.assets.json` and add them as `MetadataReference` entries.

The `project.assets.json` file is written by `dotnet restore` to the project's `obj/` directory. It contains the full transitive dependency graph with resolved assembly paths per target framework.

## Goals / Non-Goals

**Goals:**
- Parse `project.assets.json` to extract resolved NuGet assembly paths for a given target framework
- Merge project references with framework references in the Roslyn compilation
- Expose `--project` option through CLI → Node.js bridge → Shiki/EC integrations
- Populate `TwohashMeta.Packages` with resolved package info
- Optionally run `dotnet restore` if assets file is missing

**Non-Goals:**
- MSBuild API integration (too heavy, version-sensitive)
- File-based apps / `#:package` directives (future change, requires .NET 10)
- Complog support (future change, separate architecture)
- Custom NuGet resolution from inline markers
- Supporting `<ProjectReference>` (only `<PackageReference>`)

## Decisions

### 1. Parse project.assets.json directly with System.Text.Json

**Alternatives considered:**
- (a) MSBuild API — evaluates the project properly but is heavy, slow, and version-coupled
- (b) Run `dotnet build` and scrape output — too slow, side effects
- (c) NuGet client libraries — adds large dependency tree

**Decision: Direct JSON parsing.** The `project.assets.json` format is stable and well-structured. We only need the `targets` section to find assembly paths and the `libraries` section for package metadata. System.Text.Json is already available — no new dependencies.

### 2. Accept project path as directory or .csproj file

The `--project` option accepts either a `.csproj` file path or a directory containing exactly one `.csproj`. This mirrors `dotnet build` behavior. The resolver locates `obj/project.assets.json` relative to the project directory.

### 3. Target framework resolution order

When `--project` is specified:
1. Use `--framework` if explicitly provided
2. Otherwise, read the first target framework from `project.assets.json`'s `targets` keys
3. Framework references come from the SDK matching the resolved TFM

This means `--framework` and `--project` work together — the project provides packages, the framework flag overrides the TFM if needed.

### 4. Auto-restore with opt-out

If `project.assets.json` doesn't exist, the CLI runs `dotnet restore` on the project before proceeding. A `--no-restore` flag skips this. This matches the `dotnet build` convention and avoids confusing "assets file not found" errors for users who forgot to restore.

Auto-restore only happens at the CLI layer, not in TwoHash.Core — the core library expects a resolved assets file path.

### 5. Assembly path resolution from assets

The `project.assets.json` `targets` section maps `{tfm}` → package entries. Each entry has a `compile` or `runtime` dictionary with relative DLL paths. These are relative to the NuGet global packages folder (found via `packageFolders` in the assets file).

We use `compile` entries (reference assemblies) when available, falling back to `runtime` entries. This matches how MSBuild resolves references.

## Risks / Trade-offs

- **project.assets.json format stability** → The format has been stable since NuGet 4.0 (.NET Core 2.0). It's not officially documented as a public API, but it's widely relied upon by tooling. Risk is low.
- **Auto-restore modifies disk state** → Running `dotnet restore` writes to `obj/`. Mitigated by the `--no-restore` opt-out flag.
- **TFM mismatch** → If `--framework net9.0` is specified but the project targets `net8.0`, packages may not resolve. We'll warn to stderr and attempt best-effort resolution.
- **Large transitive graphs** → Projects with many transitive dependencies will load many assemblies, increasing memory and startup time. This is a build-time operation so acceptable, but worth noting.
