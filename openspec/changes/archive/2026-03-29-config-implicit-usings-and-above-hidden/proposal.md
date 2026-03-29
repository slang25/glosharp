## Why

In real-world blog integrations (~50 C# code blocks), roughly 60% of `// ---cut---` usage is just adding common usings like `using System.Text;`. A config-level `implicitUsings` eliminates this boilerplate entirely. Additionally, `// ---cut---` is not self-documenting — it's unclear which direction it "cuts" to someone encountering it for the first time. Meanwhile, `langVersion` and `nullable` are currently only settable via per-block `@langVersion`/`@nullable` markers — adding them to config provides project-wide defaults at zero runtime cost.

## What Changes

- Add `implicitUsings` string array to `twohash.config.json` — when specified, these **replace** the built-in default global usings entirely (matching .NET SDK semantics). When omitted, the current defaults apply.
- Add `langVersion` and `nullable` string properties to `twohash.config.json` — config sets the baseline, per-block `@langVersion`/`@nullable` markers override.
- Add `// @above-hidden` as a new directive that behaves identically to `// ---cut---` — everything above the line is hidden from output but included in compilation.
- Support both `// ---cut---` and `// @above-hidden` simultaneously. Document `@above-hidden` as the canonical name going forward.

## Capabilities

### New Capabilities
- `config-implicit-usings`: Configurable implicit usings via `twohash.config.json` that replace the built-in defaults when specified
- `above-hidden-directive`: New `// @above-hidden` directive as a self-documenting alternative to `// ---cut---`
- `config-lang-nullable`: Config-level `langVersion` and `nullable` defaults, overridable by per-block markers

### Modified Capabilities
- `config-file-loading`: Add `implicitUsings`, `langVersion`, and `nullable` properties to the config schema
- `marker-parsing`: Add `// @above-hidden` as an additional recognized cut marker pattern

## Impact

- `TwohashConfig` model gains `implicitUsings`, `langVersion`, and `nullable` properties
- `TwohashProcessorOptions` gains `ImplicitUsings`, `LangVersion`, and `Nullable` properties
- `TwohashProcessor` uses config usings to replace defaults when specified; uses config `langVersion`/`nullable` as fallback before marker overrides
- `MarkerParser` regex updated to accept both `---cut---` and `@above-hidden`
- CLI passes new config properties through to processor options
- Node bridge: no changes (config loaded by CLI)
- Expressive Code plugin: no changes (delegates to CLI)
