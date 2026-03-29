## Context

TwoHash already has a `twohash.config.json` with auto-discovery (walking up parent directories) and a `MarkerParser` that handles directives like `// ---cut---`, `// @hide`, `// @show`, etc. The processor prepends hardcoded global usings (`System`, `System.Collections.Generic`, `System.IO`, `System.Linq`, `System.Net.Http`, `System.Threading`, `System.Threading.Tasks`) as a separate syntax tree before compilation.

`langVersion` and `nullable` are currently only settable via per-block `@langVersion`/`@nullable` markers. There is no config-level default for these — the processor defaults to `LanguageVersion.Latest` and `NullableContextOptions.Enable`.

The current `// ---cut---` syntax is not self-documenting. The global usings list is not configurable — users who need additional or different usings must add them via `// ---cut---` blocks in every code sample.

## Goals / Non-Goals

**Goals:**
- Allow users to configure implicit usings in `twohash.config.json` (replacing defaults when specified)
- Add `langVersion` and `nullable` as config-level defaults
- Introduce `// @above-hidden` as a self-documenting alternative to `// ---cut---`
- Support both cut syntaxes simultaneously for backwards compatibility

**Non-Goals:**
- Removing or deprecating `// ---cut---` (keep both working indefinitely for now)
- Config-level `packages` (Tier 2 — requires NuGet restore orchestration, separate work)
- CLI flags for implicit usings, langVersion, or nullable (config-only for these; markers provide per-block overrides)

## Decisions

### 1. `implicitUsings` replaces defaults when specified

When `implicitUsings` is present in config (even as an empty array), it **replaces** the built-in `DefaultGlobalUsings` entirely. When omitted, the current defaults apply unchanged.

This matches how `<ImplicitUsings>` works in real .NET projects — the SDK provides a set, you can disable or replace it. Users see exactly what they get.

**Alternative considered**: Additive (merge with defaults) — rejected because it's harder to reason about. If a user specifies `["System.Text.Json"]`, do they get 8 usings or 1? Replace semantics are predictable.

### 2. `langVersion` / `nullable` in config with marker overrides

Config sets the project-wide baseline. Per-block `@langVersion`/`@nullable` markers override for individual blocks. This follows the same precedence pattern as `framework` (CLI > config > default) but with markers as the per-block override mechanism.

**Precedence**: marker > config > hardcoded default (`Latest` / `Enable`).

**Implementation**: Pass config values through `TwohashProcessorOptions`. In `TwohashProcessor.ProcessAsync`, use marker value if present, else config value, else default. The existing marker parsing in `MarkerParser` is unchanged.

### 3. Single regex matches both `---cut---` and `@above-hidden`

The `CutMarkerPattern` regex becomes `^\s*//\s*(@above-hidden|---cut---)\s*$`. All downstream logic (hiding lines above, stripping from compilation code) is unchanged. No new marker type needed — both syntaxes produce the identical effect.

### 4. No deprecation warnings for `---cut---`

Both syntaxes are first-class. Documentation will recommend `@above-hidden` but `---cut---` remains fully supported with no console warnings.

## Risks / Trade-offs

- **Replace vs extend confusion**: Users might expect `implicitUsings` to add to defaults. Clear documentation needed. → Mitigation: Document that omitting the property keeps defaults; specifying it replaces them.
- **Empty array removes all implicit usings**: `"implicitUsings": []` means no global usings at all. This is valid (user wants explicit control) but could surprise if done accidentally. → Mitigation: This is the expected .NET SDK behavior; no special handling needed.
- **Config discovery scope**: All config properties apply to all code blocks under a config file. Different subdirectories can have different configs by placing multiple `twohash.config.json` files. → Acceptable; matches existing `framework` behavior.
