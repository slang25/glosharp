## 1. Project Setup

- [x] 1.1 Create .NET solution with `GloSharp.Core` class library and `GloSharp.Cli` console app projects targeting net8.0
- [x] 1.2 Add NuGet references: `Microsoft.CodeAnalysis.CSharp` and `Microsoft.CodeAnalysis.CSharp.Features` to Core project
- [x] 1.3 Configure CLI project as a dotnet tool (pack as `glosharp` global tool)
- [x] 1.4 Initialize npm workspace with packages: `glosharp`, `@glosharp/shiki`, `@glosharp/expressive-code`
- [x] 1.5 Set up TypeScript build for all npm packages

## 2. Marker Parsing

- [x] 2.1 Implement marker scanner that identifies `^?`, `// @errors:`, `// @noErrors`, `// ---cut---`, `// @hide`, `// @show` lines
- [x] 2.2 Implement `^?` column position calculation (find `^` column in comment, map to preceding code line)
- [x] 2.3 Implement `// @errors:` parser supporting comma-separated error codes
- [x] 2.4 Implement marker line removal and position offset map builder
- [x] 2.5 Implement cut marker processing (split source into hidden/visible, track hidden ranges)
- [x] 2.6 Implement `@hide`/`@show` directive processing
- [x] 2.7 Write unit tests for all marker parsing scenarios

## 3. Roslyn Compilation

- [x] 3.1 Implement framework reference assembly resolver (`FindFrameworkRefPath` for SDK packs directory)
- [x] 3.2 Implement `CSharpCompilation` creation with framework refs, `OutputKind.ConsoleApplication`, and default global usings
- [x] 3.3 Implement syntax node finder at position (line/character → absolute position → FindToken → walk to meaningful node)
- [x] 3.4 Implement hover extraction: `GetSymbolInfo`/`GetDeclaredSymbol` → `ToDisplayParts()` → structured parts with kind mapping
- [x] 3.5 Implement XML doc comment extraction via `GetDocumentationCommentXml()`
- [x] 3.6 Implement overload count detection for method symbols
- [x] 3.7 Implement diagnostic collection with expected-error matching against `@errors` declarations
- [x] 3.8 Write unit tests for hover extraction, diagnostics, and edge cases (nullable, generics, extension methods)

## 4. JSON Output

- [x] 4.1 Define `GloSharpResult` model classes: `GloSharpResult`, `GloSharpHover`, `GloSharpError`, `GloSharpDisplayPart`, `GloSharpMeta`
- [x] 4.2 Implement `SymbolDisplayPartKind` → JSON kind string mapping
- [x] 4.3 Implement JSON serialization matching the data format spec (camelCase, empty arrays not null)
- [x] 4.4 Write integration test: end-to-end from source with markers to validated JSON output

## 5. CLI Tool

- [x] 5.1 Implement `process` command: accept file path argument, invoke core, write JSON to stdout
- [x] 5.2 Implement stdin input mode (`--stdin` flag)
- [x] 5.3 Implement `--framework` option for target framework selection
- [x] 5.4 Implement exit code logic: 0 on success, non-zero on unexpected errors
- [x] 5.5 Implement `verify` command: scan directory for `.cs` files, process each, report failures to stderr
- [x] 5.6 Ensure all non-JSON output (errors, warnings) goes to stderr only
- [x] 5.7 Write CLI integration tests

## 6. Node.js Bridge

- [x] 6.1 Define TypeScript interfaces: `GloSharpResult`, `GloSharpHover`, `GloSharpError`, `GloSharpDisplayPart`, `GloSharpMeta`, `GloSharpOptions`
- [x] 6.2 Implement `createGloSharp()` factory with CLI auto-detection and custom executable path
- [x] 6.3 Implement `process()` method: spawn CLI child process, pass code via stdin or file arg, parse JSON output
- [x] 6.4 Implement result caching by source code hash
- [x] 6.5 Implement error handling: CLI not found, non-zero exit, invalid JSON
- [x] 6.6 Write unit tests with mocked CLI

## 7. Shiki Transformer

- [x] 7.1 Implement `transformerGloSharp()` factory returning Shiki transformer object
- [x] 7.2 Implement `preprocess` hook: detect `csharp`/`cs` blocks with markers, invoke bridge, return cleaned code
- [x] 7.3 Implement `root` hook: walk HAST tree, match token positions to hover data
- [x] 7.4 Implement hover popup HAST injection with CSS anchor positioning (`anchor-name`, `position-anchor`, `inset-area`)
- [x] 7.5 Implement error annotation HAST injection (underline span + error message div)
- [x] 7.6 Implement display parts rendering with CSS classes per kind (`glosharp-keyword`, `glosharp-className`, etc.)
- [x] 7.7 Add default CSS stylesheet for popups, error underlines, and part kind colors
- [x] 7.8 Write integration tests with Shiki

## 8. Expressive Code Plugin

- [x] 8.1 Implement `pluginGloSharp()` factory returning EC plugin object
- [x] 8.2 Implement `preprocessCode` hook: detect markers, invoke bridge, strip markers from code
- [x] 8.3 Implement `GloSharpHoverAnnotation` class extending `ExpressiveCodeAnnotation` with `inlineRange` and `render()`
- [x] 8.4 Implement `GloSharpErrorAnnotation` class for error underlines and messages
- [x] 8.5 Implement `annotateCode` hook: create annotations from glosharp result
- [x] 8.6 Implement `postprocessRenderedBlock` hook: inject popup HTML with CSS anchor positioning
- [x] 8.7 Define `styleSettings` for theme-aware popup and error colors
- [x] 8.8 Implement `baseStyles` using `context.cssVar()` for light/dark theme support
- [x] 8.9 Write integration tests with Expressive Code

## 9. End-to-End Testing

- [x] 9.1 Create sample `.cs` files covering: local variables, method calls, overloads, nullable types, error expectations, cut markers, hide/show
- [x] 9.2 End-to-end test: source file → CLI → JSON → Shiki transformer → rendered HTML with popups
- [x] 9.3 End-to-end test: source file → CLI → JSON → EC plugin → rendered HTML with annotations
- [x] 9.4 Visual smoke test: render sample output in browser, verify popup positioning and styling
