## 1. @above-hidden directive

- [x] 1.1 Update `CutMarkerPattern` regex in `MarkerParser.cs` to match both `// ---cut---` and `// @above-hidden`
- [x] 1.2 Update `GetCompilationCode()` in `MarkerParser.cs` to strip both syntaxes from compilation code
- [x] 1.3 Add unit tests for `// @above-hidden` in `MarkerParserTests.cs` (basic, indented, coexistence with `---cut---`)
- [x] 1.4 Update sample file `samples/cut-marker.cs` to show `@above-hidden` syntax

## 2. Config schema additions

- [x] 2.1 Add `ImplicitUsings` (`string[]?`), `LangVersion` (`string?`), and `Nullable` (`string?`) properties to `GloSharpConfig` in `ConfigLoader.cs`
- [x] 2.2 Add `ImplicitUsings` (`string[]?`), `LangVersion` (`string?`), and `Nullable` (`string?`) properties to `GloSharpProcessorOptions`
- [x] 2.3 Pass config `ImplicitUsings`, `LangVersion`, and `Nullable` through to `GloSharpProcessorOptions` in `Program.cs`

## 3. Processor: implicit usings (replace semantics)

- [x] 3.1 In `GloSharpProcessor.ProcessAsync`, when `options.ImplicitUsings` is non-null use it instead of `DefaultGlobalUsings`; when null, keep defaults
- [x] 3.2 Add unit tests for implicit usings replacement in `GloSharpProcessorTests.cs` (custom set replaces defaults, empty array removes all, null keeps defaults)

## 4. Processor: langVersion and nullable from config

- [x] 4.1 In `GloSharpProcessor.ProcessAsync`, use `options.LangVersion` as fallback when no `@langVersion` marker is present (precedence: marker > config > default)
- [x] 4.2 In `GloSharpProcessor.ProcessAsync`, use `options.Nullable` as fallback when no `@nullable` marker is present (precedence: marker > config > default)
- [x] 4.3 Add unit tests for config langVersion/nullable with and without per-block marker overrides

## 5. Config loading tests

- [x] 5.1 Add `ConfigLoaderTests` for parsing `implicitUsings`, `langVersion`, and `nullable` from JSON (present, absent, empty)
