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
    public string? ComplogPath { get; init; }
    public string? ComplogProject { get; init; }
    public string[]? ImplicitUsings { get; init; }
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
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
        var processResult = await ProcessWithContextAsync(source, options);
        return processResult.Result;
    }

    public async Task<TwohashProcessResult> ProcessWithContextAsync(string source, TwohashProcessorOptions? options = null)
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
                options.ComplogPath ?? options.ProjectPath);

            var cached = resultCache.TryGet(resultCacheKey);
            if (cached != null)
            {
                // On cache hit, we still need compilation context for classification.
                // Fall through to build it, but use the cached result.
            }
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

        // 3. Build global usings (config replaces defaults when specified)
        var effectiveUsings = options?.ImplicitUsings ?? DefaultGlobalUsings;
        var globalUsings = string.Join('\n', effectiveUsings.Select(u => $"global using {u};"));

        // 4. Resolve language version and nullable context (precedence: marker > config > default)
        var resolvedLangVersion = LanguageVersion.Latest;
        var resolvedNullable = NullableContextOptions.Enable;
        var validationErrors = new List<TwohashError>();

        // Apply config-level defaults first
        if (options?.LangVersion != null)
        {
            var mapped = CompilationOptionsMapper.MapLangVersion(options.LangVersion);
            if (mapped == null)
            {
                validationErrors.Add(new TwohashError
                {
                    Line = 0, Character = 0, Length = 0,
                    Code = "TH0001",
                    Message = $"Invalid language version '{options.LangVersion}'. Valid values: {CompilationOptionsMapper.ValidLangVersions}",
                    Severity = "error",
                    Expected = false,
                });
            }
            else
            {
                resolvedLangVersion = mapped.Value;
            }
        }

        if (options?.Nullable != null)
        {
            var mapped = CompilationOptionsMapper.MapNullable(options.Nullable);
            if (mapped == null)
            {
                validationErrors.Add(new TwohashError
                {
                    Line = 0, Character = 0, Length = 0,
                    Code = "TH0002",
                    Message = $"Invalid nullable context '{options.Nullable}'. Valid values: {CompilationOptionsMapper.ValidNullableValues}",
                    Severity = "error",
                    Expected = false,
                });
            }
            else
            {
                resolvedNullable = mapped.Value;
            }
        }

        // Per-block markers override config
        if (markers.LangVersion != null)
        {
            var mapped = CompilationOptionsMapper.MapLangVersion(markers.LangVersion);
            if (mapped == null)
            {
                validationErrors.Add(new TwohashError
                {
                    Line = 0, Character = 0, Length = 0,
                    Code = "TH0001",
                    Message = $"Invalid language version '{markers.LangVersion}'. Valid values: {CompilationOptionsMapper.ValidLangVersions}",
                    Severity = "error",
                    Expected = false,
                });
            }
            else
            {
                resolvedLangVersion = mapped.Value;
            }
        }

        if (markers.Nullable != null)
        {
            var mapped = CompilationOptionsMapper.MapNullable(markers.Nullable);
            if (mapped == null)
            {
                validationErrors.Add(new TwohashError
                {
                    Line = 0, Character = 0, Length = 0,
                    Code = "TH0002",
                    Message = $"Invalid nullable context '{markers.Nullable}'. Valid values: {CompilationOptionsMapper.ValidNullableValues}",
                    Severity = "error",
                    Expected = false,
                });
            }
            else
            {
                resolvedNullable = mapped.Value;
            }
        }

        if (validationErrors.Count > 0)
        {
            var errorResult = new TwohashResult
            {
                Code = markers.ProcessedCode,
                Original = originalSourceWithDirectives,
                Hovers = [],
                Errors = validationErrors,
                Meta = new TwohashMeta
                {
                    TargetFramework = targetFramework ?? "net8.0",
                    CompileSucceeded = false,
                    LangVersion = markers.LangVersion,
                    Nullable = markers.Nullable,
                },
            };
            var errorParseOptions = new CSharpParseOptions(LanguageVersion.Latest);
            var errorTree = CSharpSyntaxTree.ParseText(compilationCode, errorParseOptions);
            var errorCompilation = CSharpCompilation.Create("TwohashSnippet", [errorTree],
                FrameworkResolver.GetFrameworkReferences("net8.0"),
                new CSharpCompilationOptions(OutputKind.ConsoleApplication));
            return new TwohashProcessResult
            {
                Result = errorResult,
                Compilation = errorCompilation,
                SyntaxTree = errorTree,
            };
        }

        // Parse and compile
        var parseOptions = new CSharpParseOptions(resolvedLangVersion);
        var globalUsingsTree = CSharpSyntaxTree.ParseText(globalUsings, parseOptions, path: "__GlobalUsings.cs");
        var tree = CSharpSyntaxTree.ParseText(compilationCode, parseOptions);

        // Resolve references: complog > project path > file-based app directives > framework-only
        ProjectAssetsResult? projectAssets = null;
        ComplogResolutionResult? complogResult = null;
        var resolvedFramework = targetFramework ?? "net8.0";
        string? assetsFilePath = null;

        if (options?.ComplogPath != null)
        {
            // Complog takes highest priority — bypasses all other resolution
            var complogLastWrite = File.GetLastWriteTimeUtc(options.ComplogPath).Ticks.ToString();
            var complogContextKey = CompilationContextCache.ComputeKey(
                options.ComplogPath,
                null,
                $"complog:{options.ComplogProject ?? ""}:{complogLastWrite}");

            var complogRefs = _contextCache.GetOrAdd(complogContextKey, () =>
            {
                using var resolver = ComplogResolver.Open(options.ComplogPath);
                complogResult = resolver.Resolve(options.ComplogProject);
                return complogResult.References;
            });

            // If complogResult was populated in the factory, use it; otherwise re-resolve for metadata
            if (complogResult == null)
            {
                using var resolver = ComplogResolver.Open(options.ComplogPath);
                complogResult = resolver.Resolve(options.ComplogProject);
            }

            resolvedFramework = complogResult.TargetFramework;

            // Use complog's parse options for language version if no marker override
            if (markers.LangVersion == null)
                resolvedLangVersion = complogResult.ParseOptions.LanguageVersion;
            if (markers.Nullable == null)
                resolvedNullable = complogResult.CompilationOptions.NullableContextOptions;

            // Re-parse with potentially updated options
            parseOptions = new CSharpParseOptions(resolvedLangVersion);
            globalUsingsTree = CSharpSyntaxTree.ParseText(globalUsings, parseOptions, path: "__GlobalUsings.cs");
            tree = CSharpSyntaxTree.ParseText(compilationCode, parseOptions);

            var complogCompilation = CSharpCompilation.Create(
                "TwohashSnippet",
                [tree, globalUsingsTree],
                complogRefs,
                new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                    .WithNullableContextOptions(resolvedNullable));

            return await BuildResult(complogCompilation, tree, markers, compilationCode, globalUsings,
                complogRefs, resolvedLangVersion, resolvedNullable, resolvedFramework,
                complogResult.Packages, fileDirectives, originalSourceWithDirectives,
                options.ComplogPath, resultCache, resultCacheKey);
        }

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
                .WithNullableContextOptions(resolvedNullable));

        // Build meta: prefer directive-derived packages if present, else from project assets
        var packages = fileDirectives.HasDirectives
            ? fileDirectives.GetPackageReferences()
            : projectAssets?.Packages ?? [];

        return await BuildResult(compilation, tree, markers, compilationCode, globalUsings,
            references, resolvedLangVersion, resolvedNullable, resolvedFramework,
            packages, fileDirectives, originalSourceWithDirectives,
            null, resultCache, resultCacheKey);
    }

    private async Task<TwohashProcessResult> BuildResult(
        CSharpCompilation compilation,
        SyntaxTree tree,
        MarkerParseResult markers,
        string compilationCode,
        string globalUsings,
        List<MetadataReference> references,
        LanguageVersion resolvedLangVersion,
        NullableContextOptions resolvedNullable,
        string resolvedFramework,
        List<PackageReference> packages,
        FileDirectiveResult fileDirectives,
        string originalSourceWithDirectives,
        string? complogPath,
        ResultCache? resultCache,
        string? resultCacheKey)
    {
        // Check if we have a cached result (skip extraction)
        if (resultCache != null && resultCacheKey != null)
        {
            var cached = resultCache.TryGet(resultCacheKey);
            if (cached != null)
            {
                return new TwohashProcessResult
                {
                    Result = cached,
                    Compilation = compilation,
                    SyntaxTree = tree,
                };
            }
        }

        var model = compilation.GetSemanticModel(tree);

        // Extract hovers
        var hovers = ExtractHovers(markers, tree, model, compilationCode);

        // Extract diagnostics
        var (errors, compileSucceeded) = ExtractDiagnostics(compilation, model, markers, compilationCode);

        // Extract completions (only if ^| markers present)
        var completions = markers.CompletionQueries.Count > 0
            ? await ExtractCompletions(markers, compilationCode, references, globalUsings, resolvedLangVersion, resolvedNullable)
            : [];

        // Build highlights from parsed directives
        var processedLines = markers.ProcessedCode.Split('\n');
        var highlights = markers.Highlights
            .Where(h => h.TargetOriginalLine >= 0 && h.TargetOriginalLine < processedLines.Length)
            .Select(h => new TwohashHighlight
            {
                Line = h.TargetOriginalLine,
                Character = 0,
                Length = processedLines[h.TargetOriginalLine].Length,
                Kind = h.Kind,
            })
            .ToList();

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
                LangVersion = markers.LangVersion,
                Nullable = markers.Nullable,
                Complog = complogPath,
            },
        };

        // Write to disk cache on miss
        if (resultCache != null && resultCacheKey != null)
        {
            resultCache.Set(resultCacheKey, result);
        }

        return new TwohashProcessResult
        {
            Result = result,
            Compilation = compilation,
            SyntaxTree = tree,
        };
    }

    private async Task<List<TwohashCompletion>> ExtractCompletions(
        MarkerParseResult markers,
        string compilationCode,
        List<MetadataReference> references,
        string globalUsings,
        LanguageVersion langVersion,
        NullableContextOptions nullableContext)
    {
        var completions = new List<TwohashCompletion>();
        var compilationLines = compilationCode.Split('\n');

        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        using var workspace = new AdhocWorkspace(host);
        var project = workspace.AddProject("TwohashCompletion", LanguageNames.CSharp);
        project = project
            .WithCompilationOptions(new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(nullableContext))
            .WithParseOptions(new CSharpParseOptions(langVersion));
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
        var persistentHovers = ExtractPersistentHovers(markers, tree, model, compilationCode);
        var autoHovers = ExtractAllHovers(markers, tree, model, compilationCode, persistentHovers);

        // Merge: persistent hovers first, then auto-hovers (already deduplicated)
        var merged = new List<TwohashHover>(persistentHovers.Count + autoHovers.Count);
        merged.AddRange(persistentHovers);
        merged.AddRange(autoHovers);
        return merged;
    }

    private List<TwohashHover> ExtractPersistentHovers(
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
            var originalLine = markers.LineMap[query.OriginalLine];
            var compilationLine = FindCompilationLine(compilationCode, originalLine, markers);
            if (compilationLine < 0) continue;

            var position = GetAbsolutePosition(compilationLines, compilationLine, query.Column);
            if (position < 0 || position >= compilationCode.Length) continue;

            var token = root.FindToken(position);
            if (token == default) continue;

            var hover = BuildHoverFromToken(token, model, query.OriginalLine, persistent: true);
            if (hover != null)
                hovers.Add(hover);
        }

        return hovers;
    }

    private List<TwohashHover> ExtractAllHovers(
        MarkerParseResult markers,
        SyntaxTree tree,
        SemanticModel model,
        string compilationCode,
        List<TwohashHover> persistentHovers)
    {
        var hovers = new List<TwohashHover>();
        var root = tree.GetCompilationUnitRoot();

        // Build set of persistent hover positions for deduplication
        var persistentPositions = new HashSet<(int Line, int Character)>();
        foreach (var ph in persistentHovers)
            persistentPositions.Add((ph.Line, ph.Character));

        // Build set of visible original lines (lines that appear in processed output)
        var visibleOriginalLines = new HashSet<int>(markers.LineMap);

        foreach (var token in root.DescendantTokens())
        {
            // Skip tokens that won't have meaningful hover info
            if (token.IsKind(SyntaxKind.SemicolonToken) ||
                token.IsKind(SyntaxKind.OpenBraceToken) ||
                token.IsKind(SyntaxKind.CloseBraceToken) ||
                token.IsKind(SyntaxKind.OpenParenToken) ||
                token.IsKind(SyntaxKind.CloseParenToken) ||
                token.IsKind(SyntaxKind.CommaToken) ||
                token.IsKind(SyntaxKind.DotToken) ||
                token.IsKind(SyntaxKind.EqualsToken) ||
                token.IsKind(SyntaxKind.EndOfFileToken) ||
                token.IsKind(SyntaxKind.StringLiteralToken) ||
                token.IsKind(SyntaxKind.Utf8StringLiteralToken) ||
                token.IsKind(SyntaxKind.NumericLiteralToken) ||
                token.IsKind(SyntaxKind.CharacterLiteralToken) ||
                token.IsKind(SyntaxKind.InterpolatedStringTextToken) ||
                token.IsKind(SyntaxKind.InterpolatedStringStartToken) ||
                token.IsKind(SyntaxKind.InterpolatedStringEndToken))
                continue;

            var tokenLineSpan = token.GetLocation().GetLineSpan();
            var compilationLine = tokenLineSpan.StartLinePosition.Line;

            // Map compilation line to processed line
            var processedLine = MapCompilationLineToProcessed(compilationLine, markers, compilationCode);
            if (processedLine < 0) continue;

            // Check if this compilation line maps to a visible original line
            var originalLine = markers.LineMap.Length > processedLine ? markers.LineMap[processedLine] : -1;
            if (originalLine < 0 || !visibleOriginalLines.Contains(originalLine)) continue;

            var tokenCharacter = tokenLineSpan.StartLinePosition.Character;

            // Skip if a persistent hover already covers this position
            if (persistentPositions.Contains((processedLine, tokenCharacter)))
                continue;

            var hover = BuildHoverFromToken(token, model, processedLine, persistent: false);
            if (hover != null)
                hovers.Add(hover);
        }

        return hovers;
    }

    private static TwohashHover? BuildHoverFromToken(SyntaxToken token, SemanticModel model, int line, bool persistent)
    {
        // Keywords like case, break, switch, return, if, else have no semantic symbol.
        // GetMeaningfulNode walks up to ancestor declarations (e.g. MethodDeclarationSyntax)
        // which produces misleading hovers showing the containing method.
        // Allow: predefined type keywords (int, string, void) which have PredefinedTypeSyntax,
        // contextual keywords like 'var' which resolve via IdentifierNameSyntax,
        // and expression keywords like 'this'/'base' which resolve directly to symbols.
        if (token.IsKeyword()
            && token.Parent is not PredefinedTypeSyntax
            && token.Parent is not ThisExpressionSyntax
            && token.Parent is not BaseExpressionSyntax)
            return null;

        var node = GetMeaningfulNode(token);
        if (node == null) return null;

        var symbolInfo = model.GetSymbolInfo(node);
        var symbol = symbolInfo.Symbol ?? symbolInfo.CandidateSymbols.FirstOrDefault();

        if (symbol == null)
            symbol = model.GetDeclaredSymbol(node);

        if (symbol == null)
        {
            for (var current = node.Parent; current != null && symbol == null; current = current.Parent)
                symbol = model.GetDeclaredSymbol(current);
        }

        if (symbol == null) return null;

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

        var docs = ExtractDocComment(symbol);
        var tokenLineSpan = token.GetLocation().GetLineSpan();
        var tokenCharacter = tokenLineSpan.StartLinePosition.Character;

        return new TwohashHover
        {
            Line = line,
            Character = tokenCharacter,
            Length = token.Text.Length,
            Text = text,
            Parts = displayParts,
            Docs = docs,
            SymbolKind = SymbolDisplayPartKindMapping.ToSymbolKindString(symbol),
            TargetText = token.Text,
            OverloadCount = overloadCount,
            Persistent = persistent,
        };
    }

    private (List<TwohashError> Errors, bool CompileSucceeded) ExtractDiagnostics(
        CSharpCompilation compilation,
        SemanticModel model,
        MarkerParseResult markers,
        string compilationCode)
    {
        // Conflict detection: @suppressErrors and @noErrors are mutually exclusive
        if ((markers.SuppressAllErrors || markers.SuppressedErrorCodes.Count > 0) && markers.NoErrors)
        {
            return ([new TwohashError
            {
                Line = 0,
                Character = 0,
                Length = 1,
                Code = "TH0003",
                Message = "@suppressErrors and @noErrors cannot be used together",
                Severity = "error",
                Expected = false,
            }], false);
        }

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

            // Block-level error suppression (only for actual errors, not warnings/info)
            if (diagnostic.Severity == DiagnosticSeverity.Error)
            {
                if (markers.SuppressAllErrors)
                    continue;
                if (markers.SuppressedErrorCodes.Contains(code))
                    continue;
            }

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

            // Extract end position for multi-line spans
            var endSpan = span.EndLinePosition;
            int? endLine = null;
            int? endCharacter = null;

            if (endSpan.Line != compLine)
            {
                var processedEndLine = MapCompilationLineToProcessed(endSpan.Line, markers, compilationCode);
                if (processedEndLine >= 0)
                {
                    endLine = processedEndLine;
                    endCharacter = endSpan.Character;
                }
            }

            errors.Add(new TwohashError
            {
                Line = processedLine,
                Character = compChar,
                Length = Math.Max(1, diagnostic.Location.SourceSpan.Length),
                EndLine = endLine,
                EndCharacter = endCharacter,
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
