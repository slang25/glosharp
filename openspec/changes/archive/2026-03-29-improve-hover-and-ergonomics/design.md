## Context

GloSharp extracts hover information from C# code blocks using Roslyn's semantic model. The `BuildHoverFromToken` method in `GloSharpProcessor.cs` resolves symbols for each token through a three-step fallback: `GetSymbolInfo()` → `GetDeclaredSymbol()` → parent-walk `GetDeclaredSymbol()`. The parent-walk fallback causes keywords like `case` and `break` to incorrectly resolve to their containing method/type.

Error suppression currently works only at per-line granularity via `// @errors: CSxxxx`. For blog posts referencing unavailable frameworks (Xamarin, UIKit), this requires dozens of annotations. The `@noErrors` directive serves the opposite purpose (asserting zero errors).

The EC plugin's `pluginGloSharp()` returns a `styleSettings` plain object that is incompatible with EC 0.41's `PluginStyleSettings` type, requiring consumers to strip it manually.

## Goals / Non-Goals

**Goals:**
- Keywords without semantic meaning produce no hover (no false/misleading hovers)
- Content authors can suppress errors at block level with a single directive
- EC plugin works out-of-the-box with EC 0.41+ without consumer workarounds

**Non-Goals:**
- Changing hover behavior for non-keyword tokens
- Adding NuGet package resolution for unavailable frameworks
- Restructuring the overall hover extraction pipeline

## Decisions

### 1. Track symbol resolution source in BuildHoverFromToken

**Decision**: Add a boolean flag to distinguish whether the symbol was resolved directly (via `GetSymbolInfo`/`GetDeclaredSymbol` on the node) or via the parent-walk fallback. If the token is a keyword and the symbol came only from parent-walk, return null.

**Rationale**: This is the minimal, surgical fix. The `GetMeaningfulNode` method already handles predefined type keywords (`int`, `string`, `void`) by returning `PredefinedTypeSyntax`, which resolves directly via `GetSymbolInfo`. So the keyword check plus parent-walk detection naturally excludes control flow keywords while preserving type keywords.

**Alternative considered**: Adding an explicit keyword exclusion list. Rejected because it's fragile — new C# versions add keywords, and contextual keywords complicate things. Checking the resolution source is more robust.

### 2. Use `@suppressErrors` as the directive name

**Decision**: The new directive is `// @suppressErrors` (suppress all) or `// @suppressErrors: CS0246, CS0103` (suppress specific codes). It's a block-level directive like `@noErrors`.

**Rationale**: The name clearly communicates intent — "I know there are errors, suppress them." It's distinct from `@errors` (per-line expected errors) and `@noErrors` (assert no errors). The colon-separated variant reuses the same pattern as `@errors` for familiarity.

**Alternative considered**: `@ignoreErrors`, `@bestEffort`. Rejected because `suppress` is the standard term in Roslyn/C# tooling (e.g., `#pragma warning suppress`).

### 3. Block-level suppression applies only to error reporting, not hover extraction

**Decision**: `@suppressErrors` only affects which errors are reported. Hover extraction continues as normal — Roslyn still compiles the code and extracts whatever symbols it can. This is the "best effort" behavior: you get hovers where Roslyn can resolve symbols, and errors are silently suppressed.

**Rationale**: The goal is to make blog posts look clean despite missing references. Hovers that work should still work; errors that can't be fixed should be hidden.

### 4. Remove styleSettings from plugin return object

**Decision**: Remove the `styleSettings` property from the object returned by `pluginGloSharp()`. The styles are already embedded in `baseStyles` via CSS custom properties, making `styleSettings` redundant.

**Rationale**: The `styleSettings` object was causing a type mismatch with EC 0.41 (`PluginStyleSettings` expected, plain object given). Since `baseStyles` already defines all the CSS custom properties, removing `styleSettings` is the correct fix — it's not doing anything useful.

## Risks / Trade-offs

- **Keyword hover removal is broad**: Some users might want hovers on keywords that happen to resolve. → Mitigation: The parent-walk fallback produces misleading info (containing method, not the keyword itself), so removing it is strictly an improvement. If future demand arises, a `@hoverKeywords` opt-in could be added.
- **`@suppressErrors` could mask real issues**: Suppressing all errors means authors won't see typos or logic errors. → Mitigation: This is opt-in per block, and the primary use case is for legacy/unavailable frameworks where errors are expected. The specific-codes variant `@suppressErrors: CS0246` provides a middle ground.
- **Removing styleSettings is a minor breaking change for EC plugin API**: → Mitigation: The property was broken (caused errors in EC 0.41), so consumers already had to strip it. Removing it is the fix, not a regression.
