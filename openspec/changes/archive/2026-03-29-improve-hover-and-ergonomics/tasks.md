## 1. Fix keyword hover fallback

- [x] 1.1 In `GloSharpProcessor.BuildHoverFromToken`, track whether the symbol was resolved via parent-walk fallback vs direct resolution. If the token is a keyword (`token.IsKeyword()`) and the symbol came only from parent-walk, return null. Ensure predefined type keywords (`int`, `string`, `void`) still get hovers since they resolve via `PredefinedTypeSyntax` in `GetMeaningfulNode`.
- [x] 1.2 Add tests: `case`, `break`, `switch`, `return`, `if`, `else` produce no hovers inside a method body. Verify `int`, `string`, `void`, `var` still produce hovers.

## 2. Add @suppressErrors directive parsing

- [x] 2.1 In `MarkerParser`, add regex and parsing for `// @suppressErrors` (all errors) and `// @suppressErrors: CS0246, CS0103` (specific codes). Add `SuppressAllErrors` (bool) and `SuppressedErrorCodes` (List<string>) to `MarkerParseResult`. Ensure the directive line is removed from processed output and compilation code.
- [x] 2.2 Add tests: directive parsing for both variants, position offset mapping after removal, coexistence with `@errors` per-line directives.

## 3. Honor @suppressErrors in diagnostic extraction

- [x] 3.1 In `GloSharpProcessor.ExtractDiagnostics`, apply block-level suppression: if `SuppressAllErrors` is true, suppress all errors; if `SuppressedErrorCodes` has entries, suppress matching codes. Apply before per-line `@errors` matching. Add conflict detection for `@suppressErrors` + `@noErrors`.
- [x] 3.2 Add end-to-end tests: block with `@suppressErrors` and unavailable types produces hovers but no errors; block with `@suppressErrors: CS0246` suppresses only that code; conflict with `@noErrors` produces an error.

## 4. Fix EC plugin styleSettings

- [x] 4.1 In `packages/expressive-code/src/plugin.ts`, remove the `styleSettings` property from the plugin object returned by `pluginGloSharp()`. Verify that `baseStyles` already contains all theme-aware CSS custom properties.
- [x] 4.2 Verify the plugin works with EC 0.41+ by checking that the returned object shape matches the expected plugin interface without `styleSettings`.

## 5. Integration verification

- [x] 5.1 Run the full test suite (`dotnet run --project tests/GloSharp.Tests/`) and ensure all existing tests pass alongside new tests.
- [x] 5.2 Manually verify against the blog post code samples: pattern-matching post no longer shows `HandleAbc` hover on `case`/`break`; xamarin post can use `@suppressErrors` instead of per-line `@errors`.
