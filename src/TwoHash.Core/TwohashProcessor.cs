using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;

namespace TwoHash.Core;

public class TwohashProcessorOptions
{
    public string? TargetFramework { get; init; }
    public string? ProjectPath { get; init; }
    public string? RegionName { get; init; }
    public string? SourceFilePath { get; init; }
    public bool NoRestore { get; init; }
    public string? CacheDir { get; init; }
}

public class TwohashProcessor
{
    private readonly CompilationContextCache _contextCache;

    public TwohashProcessor(CompilationContextCache? contextCache = null)
    {
        _contextCache = contextCache ?? new CompilationContextCache();
    }

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

    public async Task<TwohashResult> ProcessAsync(string source, TwohashProcessorOptions? options = null)
    {
        // Check disk-based result cache
        ResultCache? resultCache = null;
        string? resultCacheKey = null;

        if (options?.CacheDir != null)
        {
            resultCache = new ResultCache(options.CacheDir);
            resultCacheKey = ResultCache.ComputeKey(
                source,
                options.TargetFramework ?? "net8.0",
                null, // packages are embedded in source via #: directives
                options.ProjectPath);

            var cached = resultCache.TryGet(resultCacheKey);
            if (cached != null)
                return cached;
        }

        var targetFramework = options?.TargetFramework;
        var originalSourceWithDirectives = source;

        // 0. Parse and strip #: file-based app directives (before anything else)
        var fileDirectives = FileDirectiveParser.Parse(source);
        source = fileDirectives.CleanedSource;

        // 0b. Apply region extraction if requested
        if (options?.RegionName != null)
        {
            source = RegionExtractor.ApplyRegion(source, options.RegionName);
        }

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

        // Resolve references: project path > file-based app directives > framework-only
        ProjectAssetsResult? projectAssets = null;
        var resolvedFramework = targetFramework ?? "net8.0";
        string? assetsFilePath = null;

        if (options?.ProjectPath != null)
        {
            // Explicit project path takes priority
            assetsFilePath = ProjectAssetsResolver.FindAssetsFile(options.ProjectPath);
            projectAssets = ProjectAssetsResolver.Resolve(assetsFilePath, targetFramework);
            resolvedFramework = targetFramework ?? projectAssets.TargetFramework;
        }
        else if (fileDirectives.HasDirectives && options?.SourceFilePath != null)
        {
            // File-based app mode: use SDK to resolve packages
            projectAssets = FileBasedAppResolver.ResolveReferences(
                options.SourceFilePath,
                targetFramework,
                options.NoRestore);
            resolvedFramework = targetFramework ?? projectAssets.TargetFramework;

            // Override framework from #:property TargetFramework if present
            var tfmProperty = fileDirectives.GetProperty("TargetFramework");
            if (tfmProperty != null)
                resolvedFramework = targetFramework ?? tfmProperty;
        }

        // Use compilation context cache for reference resolution
        var contextKey = CompilationContextCache.ComputeKey(
            resolvedFramework,
            projectAssets?.Packages,
            assetsFilePath);

        var references = _contextCache.GetOrAdd(contextKey, () =>
        {
            var refs = FrameworkResolver.GetFrameworkReferences(resolvedFramework);
            if (projectAssets != null)
            {
                refs.AddRange(projectAssets.References);
            }
            return refs;
        });

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

        // 7. Extract completions (only if ^| markers present)
        var completions = markers.CompletionQueries.Count > 0
            ? await ExtractCompletions(markers, compilationCode, references, globalUsings)
            : [];

        // 8. Build highlights from parsed directives
        var processedLines = markers.ProcessedCode.Split('\n');
        var highlights = markers.Highlights
            .Where(h => h.TargetOriginalLine >= 0 && h.TargetOriginalLine < processedLines.Length)
            .Select(h => new TwohashHighlight
            {
                Line = h.TargetOriginalLine, // Already remapped to processed line
                Character = 0,
                Length = processedLines[h.TargetOriginalLine].Length,
                Kind = h.Kind,
            })
            .ToList();

        // Build meta: prefer directive-derived packages if present, else from project assets
        var packages = fileDirectives.HasDirectives
            ? fileDirectives.GetPackageReferences()
            : projectAssets?.Packages ?? [];

        var result = new TwohashResult
        {
            Code = markers.ProcessedCode,
            Original = originalSourceWithDirectives,
            Hovers = hovers,
            Errors = errors,
            Completions = completions,
            Highlights = highlights,
            Meta = new TwohashMeta
            {
                TargetFramework = resolvedFramework,
                Packages = packages,
                CompileSucceeded = compileSucceeded,
                Sdk = fileDirectives.GetSdk(),
            },
        };

        // Write to disk cache on miss
        if (resultCache != null && resultCacheKey != null)
        {
            resultCache.Set(resultCacheKey, result);
        }

        return result;
    }

    private async Task<List<TwohashCompletion>> ExtractCompletions(
        MarkerParseResult markers,
        string compilationCode,
        List<MetadataReference> references,
        string globalUsings)
    {
        var completions = new List<TwohashCompletion>();
        var compilationLines = compilationCode.Split('\n');

        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        using var workspace = new AdhocWorkspace(host);
        var project = workspace.AddProject("TwohashCompletion", LanguageNames.CSharp);
        project = project
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable))
            .WithParseOptions(new CSharpParseOptions(LanguageVersion.Latest));
        foreach (var r in references)
            project = project.AddMetadataReference(r);
        project = project.AddDocument("__GlobalUsings.cs", globalUsings).Project;
        var document = project.AddDocument("snippet.cs", compilationCode);
        // Apply changes to workspace so CompletionService can see them
        workspace.TryApplyChanges(document.Project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var completionService = CompletionService.GetService(document);
        if (completionService == null) return completions;

        foreach (var query in markers.CompletionQueries)
        {
            var originalLine = markers.LineMap[query.OriginalLine];
            var compilationLine = FindCompilationLine(compilationCode, originalLine, markers);
            if (compilationLine < 0) continue;

            var position = GetAbsolutePosition(compilationLines, compilationLine, query.Column);
            if (position < 0 || position > compilationCode.Length) continue;

            var completionList = await completionService.GetCompletionsAsync(document, position);
            if (completionList == null)
            {
                completions.Add(new TwohashCompletion
                {
                    Line = query.OriginalLine,
                    Character = query.Column,
                    Items = [],
                });
                continue;
            }

            var items = completionList.ItemsList
                .Select(item => new TwohashCompletionItem
                {
                    Label = item.DisplayText,
                    Kind = item.Tags.FirstOrDefault() ?? "Unknown",
                    Detail = item.InlineDescription.Length > 0 ? item.InlineDescription : null,
                })
                .ToList();

            completions.Add(new TwohashCompletion
            {
                Line = query.OriginalLine,
                Character = query.Column,
                Items = items,
            });
        }

        return completions;
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

    private static TwohashDocComment? ExtractDocComment(ISymbol symbol)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrEmpty(xml)) return null;

        try
        {
            var doc = XDocument.Parse(xml);

            var summary = ExtractElementText(doc.Descendants("summary").FirstOrDefault());
            var returns = ExtractElementText(doc.Descendants("returns").FirstOrDefault());
            var remarks = ExtractElementText(doc.Descendants("remarks").FirstOrDefault());

            var paramElements = doc.Descendants("param")
                .Select(e => new TwohashDocParam
                {
                    Name = e.Attribute("name")?.Value ?? "",
                    Text = ExtractElementText(e) ?? "",
                })
                .Where(p => !string.IsNullOrEmpty(p.Name))
                .ToList();

            var examples = doc.Descendants("example")
                .Select(e => ExtractElementText(e))
                .Where(t => t != null)
                .Cast<string>()
                .ToList();

            var exceptions = doc.Descendants("exception")
                .Select(e => new TwohashDocException
                {
                    Type = StripCrefPrefix(e.Attribute("cref")?.Value ?? ""),
                    Text = ExtractElementText(e) ?? "",
                })
                .Where(e => !string.IsNullOrEmpty(e.Type))
                .ToList();

            // Return null if there's no meaningful content
            if (summary == null && returns == null && remarks == null
                && paramElements.Count == 0 && examples.Count == 0 && exceptions.Count == 0)
                return null;

            return new TwohashDocComment
            {
                Summary = summary,
                Params = paramElements,
                Returns = returns,
                Remarks = remarks,
                Examples = examples,
                Exceptions = exceptions,
            };
        }
        catch
        {
            return null;
        }
    }

    private static string? ExtractElementText(XElement? element)
    {
        if (element == null) return null;

        var text = string.Concat(element.Nodes().Select(ResolveNode)).Trim();
        // Normalize internal whitespace (multi-line XML docs)
        text = System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ");
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static string ResolveNode(XNode node)
    {
        return node switch
        {
            XText textNode => textNode.Value,
            XElement el => el.Name.LocalName switch
            {
                "see" => StripCrefPrefix(el.Attribute("cref")?.Value ?? el.Value),
                "seealso" => StripCrefPrefix(el.Attribute("cref")?.Value ?? el.Value),
                "paramref" => el.Attribute("name")?.Value ?? "",
                "typeparamref" => el.Attribute("name")?.Value ?? "",
                "c" => el.Value,
                _ => el.Value,
            },
            _ => "",
        };
    }

    private static string StripCrefPrefix(string cref)
    {
        // Strip documentation ID prefixes like "T:", "M:", "P:", "F:", "E:", "N:"
        if (cref.Length > 2 && cref[1] == ':' && char.IsLetter(cref[0]))
            cref = cref[2..];

        // Strip namespace, keep just the type/member name
        var lastDot = cref.LastIndexOf('.');
        return lastDot >= 0 ? cref[(lastDot + 1)..] : cref;
    }
}
