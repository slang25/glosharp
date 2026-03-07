# Roslyn APIs for symbol metadata extraction

This is the core technical research for twohash. Roslyn is the C# compiler platform, and it provides the APIs we need to extract type information, hover text, and diagnostics from C# code.

## Required NuGet packages

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" />
<!-- For QuickInfo/hover features: -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Features" Version="4.*" />
<!-- For workspace/project loading: -->
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Workspaces" Version="4.*" />
```

## The compilation pipeline

Roslyn's architecture mirrors what twohash needs:

```
Source text
    │
    ▼
SyntaxTree (parsing — no references needed)
    │
    ▼
Compilation (add assembly references)
    │
    ▼
SemanticModel (type info, symbol binding, diagnostics)
    │
    ▼
ISymbol / TypeInfo / Diagnostics (the data we extract)
```

## Step 1: Parse source code

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

string sourceCode = @"
using System;

var greeting = ""Hello, World!"";
Console.WriteLine(greeting);
";

SyntaxTree tree = CSharpSyntaxTree.ParseText(sourceCode);
CompilationUnitSyntax root = tree.GetCompilationUnitRoot();

// Check for syntax errors (no references needed)
var syntaxDiagnostics = tree.GetDiagnostics();
foreach (var diag in syntaxDiagnostics)
{
    Console.WriteLine($"Syntax error: {diag.GetMessage()} at {diag.Location}");
}
```

Parsing is fast and requires no assembly references. It gives us the syntax tree — the structure of the code. But to know what types things are, we need semantic analysis.

## Step 2: Create a compilation

```csharp
// Gather framework reference assemblies
var references = new List<MetadataReference>();

// Option A: Use the running process's assemblies (simplest but imprecise)
var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
    .Split(Path.PathSeparator);
foreach (var assemblyPath in trustedAssemblies)
{
    references.Add(MetadataReference.CreateFromFile(assemblyPath));
}

// Option B: Use specific framework reference assemblies (more controlled)
var frameworkRefDir = "/usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/9.0.0/ref/net9.0";
foreach (var dll in Directory.GetFiles(frameworkRefDir, "*.dll"))
{
    references.Add(MetadataReference.CreateFromFile(dll));
}

// Create the compilation
var compilation = CSharpCompilation.Create(
    assemblyName: "TwohashAnalysis",
    syntaxTrees: new[] { tree },
    references: references,
    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary)
        .WithNullableContextOptions(NullableContextOptions.Enable)
);
```

### Key points about Compilation

- **Immutable** — you can't modify a compilation, only create new ones via `.AddReferences()`, `.AddSyntaxTrees()`, etc.
- **Language-specific** — always use `CSharpCompilation`, not the base `Compilation` class
- **References are critical** — without the right assembly references, symbols won't resolve and you'll get null from `GetSymbolInfo()`

## Step 3: Get the semantic model

```csharp
SemanticModel model = compilation.GetSemanticModel(tree);
```

The `SemanticModel` is the workhorse. It answers:
- What does this identifier refer to? (`GetSymbolInfo`)
- What type does this expression have? (`GetTypeInfo`)
- What names are in scope at this position? (for completions)
- What are the compilation errors? (`GetDiagnostics`)

## Step 4: Extract symbol information at a position

This is the core of twohash — given a position in the code, get the hover text.

### Finding the syntax node at a position

```csharp
// Given a line and character (from a ^? marker), find the syntax node
int position = tree.GetText().Lines[lineNumber].Start + characterOffset;
SyntaxNode? node = root.FindToken(position).Parent;

// Walk up to find the most meaningful node
// (a token might be inside an IdentifierNameSyntax inside a MemberAccessExpressionSyntax)
while (node != null)
{
    var symbolInfo = model.GetSymbolInfo(node);
    if (symbolInfo.Symbol != null)
        break;

    var typeInfo = model.GetTypeInfo(node);
    if (typeInfo.Type != null)
        break;

    node = node.Parent;
}
```

### Getting symbol info (what an identifier refers to)

```csharp
SymbolInfo symbolInfo = model.GetSymbolInfo(node);
ISymbol? symbol = symbolInfo.Symbol;

if (symbol != null)
{
    // symbol.Kind — SymbolKind.Local, SymbolKind.Method, SymbolKind.NamedType, etc.
    // symbol.Name — the short name
    // symbol.ContainingType — the type that contains this member
    // symbol.ContainingNamespace — the namespace

    Console.WriteLine($"Kind: {symbol.Kind}");
    Console.WriteLine($"Name: {symbol.Name}");
}
```

### Getting type info (what type an expression evaluates to)

```csharp
TypeInfo typeInfo = model.GetTypeInfo(node);
ITypeSymbol? type = typeInfo.Type;
ITypeSymbol? convertedType = typeInfo.ConvertedType; // after implicit conversions

if (type != null)
{
    Console.WriteLine($"Type: {type.ToDisplayString()}");
    Console.WriteLine($"Nullable: {type.NullableAnnotation}");
}
```

### `GetTypeInfo` vs `GetSymbolInfo`

- Use `GetSymbolInfo` for **identifiers and names** — "what does `greeting` refer to?"
- Use `GetTypeInfo` for **expressions** — "what type is `x + y`?"
- For variable declarations, you typically want both

## Step 5: Format display text

### `ISymbol.ToDisplayString()`

This is how you produce VS-like hover text:

```csharp
// Default format
string display = symbol.ToDisplayString();
// → "System.Console.WriteLine(string?)"

// Minimal format (uses imports in scope)
string minimal = symbol.ToMinimalDisplayString(model, position);
// → "Console.WriteLine(string?)"

// Custom format
var format = new SymbolDisplayFormat(
    globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
    typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypesAndNamespaces,
    genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
    memberOptions:
        SymbolDisplayMemberOptions.IncludeType |
        SymbolDisplayMemberOptions.IncludeParameters |
        SymbolDisplayMemberOptions.IncludeContainingType,
    parameterOptions:
        SymbolDisplayParameterOptions.IncludeType |
        SymbolDisplayParameterOptions.IncludeName |
        SymbolDisplayParameterOptions.IncludeDefaultValue,
    miscellaneousOptions:
        SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
        SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
);

string formatted = symbol.ToDisplayString(format);
```

### `ToDisplayParts()` — structured hover text

This is what we need for rich rendering with syntax highlighting in the popup:

```csharp
ImmutableArray<SymbolDisplayPart> parts = symbol.ToDisplayParts();

foreach (var part in parts)
{
    Console.WriteLine($"Kind: {part.Kind}, Text: \"{part.Text}\"");
}

// For "Console.WriteLine(string? value)":
// Kind: ClassName,     Text: "Console"
// Kind: Punctuation,   Text: "."
// Kind: MethodName,    Text: "WriteLine"
// Kind: Punctuation,   Text: "("
// Kind: Keyword,       Text: "string"
// Kind: Punctuation,   Text: "?"
// Kind: Space,         Text: " "
// Kind: ParameterName, Text: "value"
// Kind: Punctuation,   Text: ")"
```

### SymbolDisplayPartKind values

The `Kind` enum includes:
- `Keyword` — `int`, `string`, `class`, `async`, `void`
- `ClassName`, `StructName`, `InterfaceName`, `EnumName`, `DelegateName` — type names
- `MethodName`, `PropertyName`, `FieldName`, `EventName` — member names
- `LocalName`, `ParameterName`, `RangeVariableName` — variable names
- `NamespaceName` — namespace names
- `Punctuation` — `(`, `)`, `<`, `>`, `.`, `,`, `?`
- `Space` — whitespace
- `Text` — plain text (e.g., descriptions)
- `LineBreak` — newline

These map directly to the `parts` array in our data format.

## Step 6: Get XML documentation

```csharp
string? docXml = symbol.GetDocumentationCommentXml();

// Returns raw XML like:
// <summary>Writes the specified string value to the standard output stream.</summary>
// <param name="value">The value to write.</param>

// For rendering, you'd parse this and extract the <summary> text
```

## Step 7: Get diagnostics (compile errors)

```csharp
// All diagnostics for the compilation
var diagnostics = compilation.GetDiagnostics();

// Or just for one file
var fileDiagnostics = model.GetDiagnostics();

foreach (var diag in fileDiagnostics)
{
    var span = diag.Location.GetLineSpan();
    Console.WriteLine($"[{diag.Id}] {diag.GetMessage()}");
    Console.WriteLine($"  Severity: {diag.Severity}");  // Error, Warning, Info, Hidden
    Console.WriteLine($"  Line: {span.StartLinePosition.Line}");
    Console.WriteLine($"  Char: {span.StartLinePosition.Character}");
    Console.WriteLine($"  Length: {diag.Location.SourceSpan.Length}");
}
```

Diagnostic severity maps to:
- `DiagnosticSeverity.Error` → compile error
- `DiagnosticSeverity.Warning` → compiler warning
- `DiagnosticSeverity.Info` → informational
- `DiagnosticSeverity.Hidden` → hidden diagnostic

## Step 8: Get completions (for `^|` markers)

Completions require the `Microsoft.CodeAnalysis.Completion` namespace:

```csharp
using Microsoft.CodeAnalysis.Completion;

// Create a workspace and document (required for completion service)
var workspace = new AdhocWorkspace();
var project = workspace.AddProject("TwohashProject", LanguageNames.CSharp);
project = project.AddMetadataReferences(references);
var document = project.AddDocument("snippet.cs", sourceCode);

var completionService = CompletionService.GetService(document);
if (completionService != null)
{
    var completions = await completionService.GetCompletionsAsync(document, position);
    if (completions != null)
    {
        foreach (var item in completions.ItemsList)
        {
            Console.WriteLine($"{item.DisplayText} ({item.Tags.FirstOrDefault()})");
        }
    }
}
```

## Complete example: extracting hover info

Putting it all together — this is essentially what twohash's core does:

```csharp
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

public record HoverInfo(
    int Line,
    int Character,
    int Length,
    string Text,
    SymbolDisplayPart[] Parts,
    string? Documentation,
    string SymbolKind,
    string TargetText
);

public static HoverInfo? GetHoverAtPosition(
    SemanticModel model,
    SyntaxTree tree,
    int line,
    int character)
{
    // Convert line/char to absolute position
    var text = tree.GetText();
    int position = text.Lines[line].Start + character;

    // Find the token at this position
    var token = tree.GetCompilationUnitRoot().FindToken(position);
    if (token == default) return null;

    var node = token.Parent;
    if (node == null) return null;

    // Try GetSymbolInfo first
    var symbolInfo = model.GetSymbolInfo(node);
    var symbol = symbolInfo.Symbol;

    // Fall back to GetDeclaredSymbol for declarations
    if (symbol == null)
        symbol = model.GetDeclaredSymbol(node);

    // Fall back to type info for expressions
    if (symbol == null)
    {
        var typeInfo = model.GetTypeInfo(node);
        symbol = typeInfo.Type;
    }

    if (symbol == null) return null;

    // Build the display text
    var displayParts = symbol.ToMinimalDisplayParts(model, position);
    var displayText = symbol.ToMinimalDisplayString(model, position);

    // Add prefix based on symbol kind
    string prefix = symbol.Kind switch
    {
        Microsoft.CodeAnalysis.SymbolKind.Local => "(local variable) ",
        Microsoft.CodeAnalysis.SymbolKind.Parameter => "(parameter) ",
        Microsoft.CodeAnalysis.SymbolKind.Field => "(field) ",
        Microsoft.CodeAnalysis.SymbolKind.Property => "(property) ",
        Microsoft.CodeAnalysis.SymbolKind.Method => "(method) ",
        Microsoft.CodeAnalysis.SymbolKind.NamedType => symbol switch
        {
            INamedTypeSymbol { TypeKind: TypeKind.Class } => "(class) ",
            INamedTypeSymbol { TypeKind: TypeKind.Interface } => "(interface) ",
            INamedTypeSymbol { TypeKind: TypeKind.Struct } => "(struct) ",
            INamedTypeSymbol { TypeKind: TypeKind.Enum } => "(enum) ",
            _ => ""
        },
        _ => ""
    };

    // Get XML documentation
    string? docs = symbol.GetDocumentationCommentXml();
    // TODO: Parse XML to extract <summary> text

    return new HoverInfo(
        Line: line,
        Character: character,
        Length: token.Span.Length,
        Text: prefix + displayText,
        Parts: displayParts.ToArray(),
        Documentation: docs,
        SymbolKind: symbol.Kind.ToString(),
        TargetText: token.Text
    );
}
```

## OmniSharp's approach

OmniSharp (the C# language server) is the reference implementation for IDE features. It uses:

1. **QuickInfoService** from `Microsoft.CodeAnalysis.Features` for hover information
2. Internally calls `GetSymbolInfo` / `GetTypeInfo` / `GetDeclaredSymbol`
3. Formats results using `SymbolDisplayFormat` with specific options
4. Returns structured `QuickInfoItem` with tagged text parts

OmniSharp's hover handler (simplified):

```csharp
// From OmniSharp's QuickInfoProvider
var quickInfoService = QuickInfoService.GetService(document);
var quickInfo = await quickInfoService.GetQuickInfoAsync(document, position);

// quickInfo.Sections contains:
//   - Description (the main type/member info)
//   - Documentation (XML doc comments)
//   - TypeParameter (generic constraints)
//   - AnonymousTypes (expanded anonymous type definitions)
//   - Usage (for keywords, shows usage patterns)
```

The `QuickInfoService` is in `Microsoft.CodeAnalysis.Features` which is a heavier dependency. For twohash, we can likely get equivalent results from `ToDisplayParts()` + `GetDocumentationCommentXml()` with less overhead.

## Symbol comparison

Use `SymbolEqualityComparer.Default` — never reference equality:

```csharp
// Correct
if (SymbolEqualityComparer.Default.Equals(symbol1, symbol2)) { ... }

// Wrong
if (symbol1 == symbol2) { ... }
```

## Overload handling

For methods with overloads:

```csharp
if (symbol is IMethodSymbol method)
{
    var containingType = method.ContainingType;
    var overloads = containingType.GetMembers(method.Name)
        .OfType<IMethodSymbol>()
        .Where(m => m.MethodKind == MethodKind.Ordinary)
        .ToList();

    if (overloads.Count > 1)
    {
        int overloadCount = overloads.Count;
        // Append "(+ N overloads)" to display text
    }
}
```

## Key architectural notes for twohash

1. **Compilation is expensive** — cache it when processing multiple snippets from the same project
2. **SemanticModel is per-tree** — one model per syntax tree, tied to a specific compilation
3. **Reference assemblies vs implementation assemblies** — use reference assemblies (from packs/) for accurate API surface; implementation assemblies may expose internals
4. **Immutability** — Roslyn objects are immutable; create new compilations/trees rather than modifying
5. **Top-level statements** — modern C# uses top-level statements (no `Main` method wrapper). Use `OutputKind.ConsoleApplication` for these, not `DynamicallyLinkedLibrary`
6. **Global usings** — .NET 6+ has implicit global usings. We need to include these for accurate resolution (or add them manually to the syntax tree)
