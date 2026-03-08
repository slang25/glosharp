using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace TwoHash.Core;

public class TwohashProcessorOptions
{
    public string? TargetFramework { get; init; }
    public string? ProjectPath { get; init; }
}

public class TwohashProcessor
{
    private static readonly string[] DefaultGlobalUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    private static readonly SymbolDisplayFormat DisplayFormat = new(
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
        localOptions: SymbolDisplayLocalOptions.IncludeType,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    public TwohashResult Process(string source, TwohashProcessorOptions? options = null)
    {
        var targetFramework = options?.TargetFramework;

        // 1. Parse markers
        var markers = MarkerParser.Parse(source);

        // 2. Get code for compilation (all code, no markers)
        var compilationCode = MarkerParser.GetCompilationCode(source);

        // 3. Build global usings
        var globalUsings = string.Join('\n', DefaultGlobalUsings.Select(u => $"global using {u};"));
        var globalUsingsTree = CSharpSyntaxTree.ParseText(globalUsings, path: "__GlobalUsings.cs");

        // 4. Parse and compile
        var parseOptions = new CSharpParseOptions(LanguageVersion.Latest);
        var tree = CSharpSyntaxTree.ParseText(compilationCode, parseOptions);

        // Resolve project references if a project path is provided
        ProjectAssetsResult? projectAssets = null;
        var resolvedFramework = targetFramework ?? "net8.0";
        if (options?.ProjectPath != null)
        {
            var assetsFile = ProjectAssetsResolver.FindAssetsFile(options.ProjectPath);
            projectAssets = ProjectAssetsResolver.Resolve(assetsFile, targetFramework);
            resolvedFramework = targetFramework ?? projectAssets.TargetFramework;
        }

        var references = FrameworkResolver.GetFrameworkReferences(resolvedFramework);
        if (projectAssets != null)
        {
            references.AddRange(projectAssets.References);
        }

        var compilation = CSharpCompilation.Create(
            "TwohashSnippet",
            [tree, globalUsingsTree],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        var model = compilation.GetSemanticModel(tree);

        // 5. Extract hovers
        var hovers = ExtractHovers(markers, tree, model, compilationCode);

        // 6. Extract diagnostics
        var (errors, compileSucceeded) = ExtractDiagnostics(compilation, model, markers, compilationCode);

        return new TwohashResult
        {
            Code = markers.ProcessedCode,
            Original = markers.OriginalCode,
            Hovers = hovers,
            Errors = errors,
            Meta = new TwohashMeta
            {
                TargetFramework = resolvedFramework,
                Packages = projectAssets?.Packages ?? [],
                CompileSucceeded = compileSucceeded,
            },
        };
    }

    private List<TwohashHover> ExtractHovers(
        MarkerParseResult markers,
        SyntaxTree tree,
        SemanticModel model,
        string compilationCode)
    {
        var hovers = new List<TwohashHover>();
        var root = tree.GetCompilationUnitRoot();
        var compilationLines = compilationCode.Split('\n');

        foreach (var query in markers.HoverQueries)
        {
            // Map processed line back to compilation line
            // The compilation code has all lines except markers
            // We need to find the position in compilation code
            var originalLine = markers.LineMap[query.OriginalLine];

            // Find this original line in the compilation code
            var compilationLine = FindCompilationLine(compilationCode, originalLine, markers);
            if (compilationLine < 0) continue;

            var position = GetAbsolutePosition(compilationLines, compilationLine, query.Column);
            if (position < 0 || position >= compilationCode.Length) continue;

            var token = root.FindToken(position);
            if (token == default) continue;

            var node = GetMeaningfulNode(token);
            if (node == null) continue;

            var symbolInfo = model.GetSymbolInfo(node);
            var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

            // Try GetDeclaredSymbol for declarations
            if (symbol == null)
            {
                symbol = model.GetDeclaredSymbol(node);
            }

            // Walk up to find a declaration
            if (symbol == null)
            {
                for (var current = node.Parent; current != null && symbol == null; current = current.Parent)
                {
                    symbol = model.GetDeclaredSymbol(current);
                }
            }

            if (symbol == null) continue;

            var parts = symbol.ToDisplayParts(DisplayFormat);
            var prefix = GetSymbolPrefix(symbol);

            var displayParts = new List<TwohashDisplayPart>();
            if (prefix != null)
            {
                displayParts.Add(new TwohashDisplayPart { Kind = "punctuation", Text = "(" });
                displayParts.Add(new TwohashDisplayPart { Kind = "text", Text = prefix });
                displayParts.Add(new TwohashDisplayPart { Kind = "punctuation", Text = ")" });
                displayParts.Add(new TwohashDisplayPart { Kind = "space", Text = " " });
            }

            foreach (var part in parts)
            {
                displayParts.Add(new TwohashDisplayPart
                {
                    Kind = SymbolDisplayPartKindMapping.ToJsonKind(part.Kind),
                    Text = part.ToString(),
                });
            }

            var text = prefix != null
                ? $"({prefix}) {symbol.ToDisplayString(DisplayFormat)}"
                : symbol.ToDisplayString(DisplayFormat);

            // Overload count for methods
            int? overloadCount = null;
            if (symbol is IMethodSymbol method)
            {
                var overloads = method.ContainingType.GetMembers(method.Name)
                    .OfType<IMethodSymbol>()
                    .Count();
                if (overloads > 1)
                {
                    overloadCount = overloads;
                    text += $" (+ {overloads - 1} overloads)";
                }
            }

            // XML doc comments
            var docs = ExtractDocComment(symbol);

            hovers.Add(new TwohashHover
            {
                Line = query.OriginalLine,
                Character = query.Column,
                Length = token.Text.Length,
                Text = text,
                Parts = displayParts,
                Docs = docs,
                SymbolKind = SymbolDisplayPartKindMapping.ToSymbolKindString(symbol),
                TargetText = token.Text,
                OverloadCount = overloadCount,
            });
        }

        return hovers;
    }

    private (List<TwohashError> Errors, bool CompileSucceeded) ExtractDiagnostics(
        CSharpCompilation compilation,
        SemanticModel model,
        MarkerParseResult markers,
        string compilationCode)
    {
        var errors = new List<TwohashError>();
        var compilationLines = compilationCode.Split('\n');

        var diagnostics = model.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Info)
            .Where(d => d.Location.IsInSource && d.Location.SourceTree?.FilePath != "__GlobalUsings.cs")
            .ToList();

        var hasUnexpectedErrors = false;

        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic.Location.GetLineSpan();
            var compLine = span.StartLinePosition.Line;
            var compChar = span.StartLinePosition.Character;

            // Map compilation line to processed line
            var processedLine = MapCompilationLineToProcessed(compLine, markers, compilationCode);
            if (processedLine < 0) continue;

            var code = diagnostic.Id;
            var expected = markers.ErrorExpectations.Any(e =>
                e.OriginalLine == processedLine && e.Codes.Contains(code));

            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                _ => "hidden",
            };

            if (severity == "error" && !expected)
                hasUnexpectedErrors = true;

            errors.Add(new TwohashError
            {
                Line = processedLine,
                Character = compChar,
                Length = Math.Max(1, diagnostic.Location.SourceSpan.Length),
                Code = code,
                Message = diagnostic.GetMessage(),
                Severity = severity,
                Expected = expected,
            });
        }

        var compileSucceeded = !hasUnexpectedErrors;

        // If @noErrors is set, any error means failure
        if (markers.NoErrors && errors.Any(e => e.Severity == "error"))
        {
            compileSucceeded = false;
        }

        return (errors, compileSucceeded);
    }

    private static int FindCompilationLine(string compilationCode, int originalLine, MarkerParseResult markers)
    {
        // The compilation code has marker lines removed
        // We need to figure out which compilation line corresponds to the original line
        var source = markers.OriginalCode;
        var origLines = source.Split('\n');
        var compLines = compilationCode.Split('\n');

        // Build a map from original line to compilation line
        var compLineIdx = 0;
        for (var i = 0; i < origLines.Length && compLineIdx < compLines.Length; i++)
        {
            if (i == originalLine)
                return compLineIdx;

            // Check if this original line appears in compilation code
            if (origLines[i] == compLines[compLineIdx])
                compLineIdx++;
        }

        return -1;
    }

    private static int MapCompilationLineToProcessed(int compLine, MarkerParseResult markers, string compilationCode)
    {
        // Find which original line this compilation line corresponds to
        var origLines = markers.OriginalCode.Split('\n');
        var compLines = compilationCode.Split('\n');

        if (compLine >= compLines.Length) return -1;

        var compLineIdx = 0;
        var originalLine = -1;
        for (var i = 0; i < origLines.Length && compLineIdx < compLines.Length; i++)
        {
            if (origLines[i] == compLines[compLineIdx])
            {
                if (compLineIdx == compLine)
                {
                    originalLine = i;
                    break;
                }
                compLineIdx++;
            }
        }

        if (originalLine < 0) return -1;

        // Now map original line to processed line
        var idx = Array.IndexOf(markers.LineMap, originalLine);
        return idx;
    }

    private static int GetAbsolutePosition(string[] lines, int line, int character)
    {
        var pos = 0;
        for (var i = 0; i < line && i < lines.Length; i++)
        {
            pos += lines[i].Length + 1; // +1 for newline
        }
        return pos + character;
    }

    private static SyntaxNode? GetMeaningfulNode(SyntaxToken token)
    {
        var node = token.Parent;
        while (node != null)
        {
            if (node is IdentifierNameSyntax ||
                node is PredefinedTypeSyntax ||
                node is GenericNameSyntax ||
                node is VariableDeclaratorSyntax ||
                node is MethodDeclarationSyntax ||
                node is PropertyDeclarationSyntax ||
                node is ParameterSyntax ||
                node is MemberAccessExpressionSyntax ||
                node is InvocationExpressionSyntax)
            {
                return node;
            }

            // For simple names, the identifier name is good enough
            if (node is SimpleNameSyntax)
                return node;

            node = node.Parent;
        }

        return token.Parent;
    }

    private static string? GetSymbolPrefix(ISymbol symbol) => symbol switch
    {
        ILocalSymbol => "local variable",
        IParameterSymbol => "parameter",
        IFieldSymbol f => f.IsConst ? "constant" : "field",
        IPropertySymbol => "property",
        IMethodSymbol m => m.IsExtensionMethod ? "extension" : "method",
        IEventSymbol => "event",
        INamedTypeSymbol nts => nts.TypeKind switch
        {
            TypeKind.Class => "class",
            TypeKind.Struct => "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => null,
        },
        INamespaceSymbol => "namespace",
        _ => null,
    };

    private static string? ExtractDocComment(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml)) return null;

        try
        {
            var doc = XDocument.Parse(xml);
            var summary = doc.Descendants("summary").FirstOrDefault();
            if (summary == null) return null;

            var text = summary.Value.Trim();
            return string.IsNullOrEmpty(text) ? null : text;
        }
        catch
        {
            return null;
        }
    }
}
