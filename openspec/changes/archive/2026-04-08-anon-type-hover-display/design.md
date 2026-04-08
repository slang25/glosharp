## Context

Roslyn represents anonymous types with compiler-generated names like `<>f__AnonymousType0<string, double[]>`. When `ISymbol.ToDisplayParts()` is called with the current `SymbolDisplayFormat`, these names flow through directly into hover text. The result is opaque and unhelpful — users see an internal compiler name rather than the shape of the type.

VS Code with C# Dev Kit handles this by replacing anonymous type names with placeholders (`'a`, `'b`, etc.) and appending a "Types:" section that expands the placeholder into the anonymous type's property shape (e.g., `'a is new { string Name, double[] Readings }`).

Currently in `GloSharpProcessor.cs`:
- `BuildHoverFromToken()` calls `symbol.ToDisplayParts(DisplayFormat)` with no post-processing
- `GetSymbolPrefix()` falls through to `null` for anonymous types (`TypeKind.Class` but not handled)
- No anonymous type detection exists anywhere in the pipeline

## Goals / Non-Goals

**Goals:**
- Replace compiler-generated anonymous type names with readable placeholders (`'a`, `'b`, etc.) in hover display parts
- Append structured type annotations mapping placeholders to their expanded shape
- Maintain consistent placeholder assignment within a single hover (same anonymous type always gets the same letter)
- Produce output that renderers (HTML, Shiki, EC) can consume to display the "Types:" section

**Non-Goals:**
- Changing how Roslyn compiles or resolves anonymous types
- Adding nullability flow annotations like "'sensors' is not null here" (separate concern)
- Modifying the `SymbolDisplayFormat` itself — this is a post-processing transformation
- Handling anonymous types in completion results (only hovers)

## Decisions

### 1. Post-process display parts rather than changing SymbolDisplayFormat

Roslyn's `SymbolDisplayFormat` doesn't have built-in support for anonymous type placeholder substitution. Rather than trying to hook into Roslyn's formatting, we'll post-process the `List<SymbolDisplayPart>` output from `ToDisplayParts()`.

**Rationale:** This keeps the Roslyn integration clean and lets us control the exact output format. It's also what OmniSharp does internally — it post-processes QuickInfo results.

**Alternative considered:** Using `SymbolDisplayFormat.WithMiscellaneousOptions(IncludeAnonymousTypes)` — but this flag doesn't exist; Roslyn doesn't offer this out of the box.

### 2. Detect anonymous types via INamedTypeSymbol.IsAnonymousType

Roslyn exposes `INamedTypeSymbol.IsAnonymousType` as a public API. This is more reliable than string-matching on compiler-generated names like `<>f__AnonymousType0`.

**Rationale:** Future-proof against compiler name changes. Cleaner, API-level detection.

### 3. Add a `typeAnnotations` field to GloSharpHover

Add a new optional field to the hover model:

```csharp
public List<GloSharpTypeAnnotation>? TypeAnnotations { get; init; }
```

Where:
```csharp
public class GloSharpTypeAnnotation
{
    public required string Name { get; init; }       // e.g., "'a"
    public required string Expansion { get; init; }  // e.g., "new { string Name, double[] Readings }"
}
```

**Rationale:** Keeping the annotation data structured rather than baking it into the `text` string lets renderers format it appropriately (e.g., dimmed text, separate line, tooltip section).

**Alternative considered:** Appending a `\nTypes:\n    'a is new { ... }` to the `text` field directly. Rejected because it couples presentation with data and breaks renderers that rely on `text` being a single symbol description.

### 4. Assign placeholder letters per-hover scope

Within a single hover's display parts, assign letters sequentially: first anonymous type encountered gets `'a`, second gets `'b`, etc. The mapping is local to each hover — there's no global registry across hovers.

**Rationale:** Keeps it simple. Each hover tooltip is self-contained. No need for cross-hover coordination.

### 5. Build expansion text from anonymous type properties

Walk `INamedTypeSymbol.GetMembers().OfType<IPropertySymbol>()` to extract properties and format them as `new { Type1 Prop1, Type2 Prop2 }`. Use the same `SymbolDisplayFormat` for property types to get consistent short names (`string` not `System.String`).

Recursively detect nested anonymous types within property types and assign them placeholders too.

## Risks / Trade-offs

**[Risk] Nested anonymous types create complex expansions** → Mitigation: Recursively assign placeholders. `'a is new { string Name, 'b Details }` with `'b is new { int Id }`. Limit nesting depth to 3 levels.

**[Risk] Display part replacement is fragile if Roslyn changes part structure** → Mitigation: Use `IsAnonymousType` for detection rather than text matching. Add defensive checks and fall back to raw display if parts don't match expected patterns.

**[Risk] Downstream renderers need updating** → Mitigation: `typeAnnotations` is optional/nullable. Renderers that don't handle it will simply not show the section — no breakage. Update renderers incrementally.
