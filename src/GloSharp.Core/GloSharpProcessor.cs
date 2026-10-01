using System.Security.Cryptography;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using Microsoft.CodeAnalysis.Host.Mef;

namespace GloSharp.Core;

/// <summary>
/// Options for one <see cref="GloSharpProcessor"/> run.
/// </summary>
/// <remarks>
/// Every property except <see cref="CacheDir"/> is part of the result-cache key (the record is
/// serialised as a whole), so a new option is automatically taken into account by the cache.
/// </remarks>
public record GloSharpProcessorOptions
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

public class GloSharpProcessor
{
    internal const string DefaultTargetFramework = FrameworkResolver.DefaultTargetFramework;
    private const string GlobalUsingsPath = "__GlobalUsings.cs";
    private const string WebSdk = "Microsoft.NET.Sdk.Web";

    private readonly CompilationContextCache _contextCache;

    public GloSharpProcessor(CompilationContextCache? contextCache = null)
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

    private static readonly string[] WebSdkGlobalUsings =
    [
        "System.Net.Http.Json",
        "Microsoft.AspNetCore.Builder",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Logging",
    ];

    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
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

    public async Task<GloSharpResult> ProcessAsync(string source, GloSharpProcessorOptions? options = null)
    {
        var processResult = await ProcessWithContextAsync(source, options);
        return processResult.Result;
    }

    public async Task<GloSharpProcessResult> ProcessWithContextAsync(string source, GloSharpProcessorOptions? options = null)
    {
        options ??= new GloSharpProcessorOptions();

        // 1. Text-only preprocessing: #: directives, region, markers. Cheap, no Roslyn binding.
        var snippet = Snippet.Prepare(source, options);

        // 2. Result cache: a hit skips reference resolution and compilation entirely. The
        //    compilation (needed only for syntax classification when rendering) is built lazily.
        ResultCache? resultCache = null;
        string? resultCacheKey = null;
        if (options.CacheDir != null)
        {
            resultCache = new ResultCache(options.CacheDir);
            resultCacheKey = ComputeResultCacheKey(source, options, snippet);

            var cached = resultCache.TryGet(resultCacheKey);
            if (cached != null)
            {
                return new GloSharpProcessResult(cached, () => BuildCompilationIgnoringErrors(snippet, options))
                {
                    FromCache = true,
                };
            }
        }

        // 3. Requested language version / nullable context (marker > config)
        var requested = ResolveRequestedSettings(snippet, options);
        if (requested.Errors.Count > 0)
        {
            var errorResult = new GloSharpResult
            {
                Code = snippet.Markers.ProcessedCode,
                Original = source,
                Hovers = [],
                Errors = requested.Errors,
                Meta = new GloSharpMeta
                {
                    TargetFramework = options.TargetFramework
                        ?? snippet.Directives.GetProperty("TargetFramework")
                        ?? DefaultTargetFramework,
                    CompileSucceeded = false,
                    Sdk = snippet.Directives.GetSdk(),
                    LangVersion = snippet.Markers.LangVersion ?? options.LangVersion,
                    Nullable = snippet.Markers.Nullable ?? options.Nullable,
                    Complog = options.ComplogPath,
                },
            };
            return new GloSharpProcessResult(errorResult, () => BuildCompilationIgnoringErrors(snippet, options));
        }

        // 4. Resolve references + compile
        var warnings = new List<string>();
        var context = ResolveContext(snippet, options, warnings);
        var built = CreateCompilation(snippet, options, context, requested);

        // 5. Extract
        var result = await BuildResultAsync(snippet, options, context, built, warnings);

        resultCache?.Set(resultCacheKey!, result);

        return new GloSharpProcessResult(result, built.Compilation, built.Tree);
    }

    // ------------------------------------------------------------------
    // Snippet preprocessing
    // ------------------------------------------------------------------

    private sealed class Snippet
    {
        public required string Source { get; init; }
        public required FileDirectiveResult Directives { get; init; }
        public required MarkerParseResult Markers { get; init; }

        public static Snippet Prepare(string source, GloSharpProcessorOptions options)
        {
            // #: file-based app directives are stripped first
            var directives = FileDirectiveParser.Parse(source);
            var text = directives.CleanedSource;

            // Region extraction is a hidden-line mask, so the rest of the file still compiles
            var regionMask = options.RegionName != null
                ? RegionExtractor.GetHiddenLineMask(text, options.RegionName)
                : null;

            return new Snippet
            {
                Source = source,
                Directives = directives,
                Markers = MarkerParser.Parse(text, regionMask),
            };
        }

        /// <summary>Maps a marker-parser input line to the line in the original input text.</summary>
        public int ToSourceLine(int inputLine) =>
            inputLine >= 0 && inputLine < Directives.LineMap.Length ? Directives.LineMap[inputLine] : inputLine;

        /// <summary>Maps a processed (rendered) line to the line in the original input text.</summary>
        public int ProcessedToSourceLine(int processedLine) => ToSourceLine(Markers.LineMap[processedLine]);
    }

    private static string ComputeResultCacheKey(string source, GloSharpProcessorOptions options, Snippet snippet)
    {
        // The source path only matters for file-based apps (#: directives are resolved relative to it)
        var keyOptions = snippet.Directives.HasDirectives ? options : options with { SourceFilePath = null };

        var fingerprints = new List<string>();
        if (options.ComplogPath != null)
            fingerprints.Add("complog:" + ResultCache.FileFingerprint(options.ComplogPath));

        if (options.ProjectPath != null)
        {
            string assets;
            try { assets = ProjectAssetsResolver.FindAssetsFile(options.ProjectPath); }
            catch (FileNotFoundException) { assets = Path.Combine(options.ProjectPath, "obj", "project.assets.json"); }
            fingerprints.Add("assets:" + ResultCache.FileFingerprint(assets));
            // The project's own built assembly (and its ProjectReferences') change hovers too
            try
            {
                var outputs = ProjectAssetsResolver.Resolve(assets, options.TargetFramework).ProjectOutputPaths;
                fingerprints.AddRange(outputs.Select(o => "output:" + ResultCache.FileFingerprint(o)));
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException) { }
        }

        return ResultCache.ComputeKey(source, keyOptions, fingerprints);
    }

    // ------------------------------------------------------------------
    // Language version / nullable
    // ------------------------------------------------------------------

    private sealed record RequestedSettings(
        LanguageVersion? LangVersion,
        NullableContextOptions? Nullable,
        List<GloSharpError> Errors);

    private static RequestedSettings ResolveRequestedSettings(Snippet snippet, GloSharpProcessorOptions options)
    {
        var errors = new List<GloSharpError>();
        var markers = snippet.Markers;

        LanguageVersion? langVersion = null;
        NullableContextOptions? nullable = null;

        // Precedence: marker > config
        if (options.LangVersion != null)
            langVersion = MapLangVersion(options.LangVersion, sourceLine: null, errors);
        if (markers.LangVersion != null)
            langVersion = MapLangVersion(markers.LangVersion, snippet.ToSourceLine(markers.LangVersionLine), errors) ?? langVersion;

        if (options.Nullable != null)
            nullable = MapNullable(options.Nullable, sourceLine: null, errors);
        if (markers.Nullable != null)
            nullable = MapNullable(markers.Nullable, snippet.ToSourceLine(markers.NullableLine), errors) ?? nullable;

        return new RequestedSettings(langVersion, nullable, errors);
    }

    private static LanguageVersion? MapLangVersion(string value, int? sourceLine, List<GloSharpError> errors)
    {
        var mapped = CompilationOptionsMapper.MapLangVersion(value);
        if (mapped == null)
        {
            errors.Add(ConfigurationError(GloSharpDiagnosticCodes.InvalidLangVersion,
                $"Invalid language version '{value}'. Valid values: {CompilationOptionsMapper.ValidLangVersions}",
                sourceLine));
        }
        return mapped;
    }

    private static NullableContextOptions? MapNullable(string value, int? sourceLine, List<GloSharpError> errors)
    {
        var mapped = CompilationOptionsMapper.MapNullable(value);
        if (mapped == null)
        {
            errors.Add(ConfigurationError(GloSharpDiagnosticCodes.InvalidNullable,
                $"Invalid nullable context '{value}'. Valid values: {CompilationOptionsMapper.ValidNullableValues}",
                sourceLine));
        }
        return mapped;
    }

    private static GloSharpError ConfigurationError(string code, string message, int? sourceLine) => new()
    {
        Line = 0,
        Character = 0,
        Length = 0,
        Code = code,
        Message = message,
        Severity = "error",
        Expected = false,
        SourceLine = sourceLine ?? 0,
        SourceCharacter = 0,
    };

    // ------------------------------------------------------------------
    // Compilation context (references + options)
    // ------------------------------------------------------------------

    private sealed class SnippetContext
    {
        public required List<MetadataReference> References { get; init; }
        public required string TargetFramework { get; init; }
        public required List<PackageReference> Packages { get; init; }
        public bool IsWeb { get; init; }
        public ComplogResolutionResult? Complog { get; init; }
    }

    private sealed record ComplogContext(ComplogResolutionResult Resolution, List<PackageReference> Packages, bool IsWeb);

    private SnippetContext ResolveContext(Snippet snippet, GloSharpProcessorOptions options, List<string> warnings)
    {
        var directives = snippet.Directives;

        // Complog / .glocontext takes highest priority — bypasses all other resolution
        if (options.ComplogPath != null)
        {
            var complog = ResolveComplog(options.ComplogPath, options.ComplogProject, options.TargetFramework);
            warnings.AddRange(complog.Resolution.Warnings);
            return new SnippetContext
            {
                References = complog.Resolution.References,
                TargetFramework = complog.Resolution.TargetFramework,
                Packages = complog.Packages,
                IsWeb = complog.IsWeb,
                Complog = complog.Resolution,
            };
        }

        ProjectAssetsResult? projectAssets = null;
        string? assetsFilePath = null;
        var packages = new List<PackageReference>();
        var targetFramework = options.TargetFramework;

        if (options.ProjectPath != null)
        {
            assetsFilePath = ProjectAssetsResolver.FindAssetsFile(options.ProjectPath);
            projectAssets = ProjectAssetsResolver.Resolve(assetsFilePath, options.TargetFramework);
            targetFramework ??= projectAssets.TargetFramework;
            packages = projectAssets.Packages;
            warnings.AddRange(projectAssets.Warnings);
        }
        else if (directives.HasDirectives)
        {
            // File-based app mode: the SDK resolves #:package / #:sdk / #:property directives
            var tfmProperty = directives.GetProperty("TargetFramework");
            try
            {
                var resolveFilePath = options.SourceFilePath ?? WriteDirectiveStub(directives);
                projectAssets = FileBasedAppResolver.ResolveReferences(
                    resolveFilePath, options.TargetFramework, options.NoRestore);
                targetFramework ??= tfmProperty ?? projectAssets.TargetFramework;
                warnings.AddRange(projectAssets.Warnings);
            }
            catch (Exception ex)
            {
                targetFramework ??= tfmProperty;
                warnings.Add(
                    "Could not resolve the file-based app directives (" +
                    string.Join(", ", directives.DirectiveLines) +
                    "); the snippet was compiled against the framework only. " +
                    ex.Message.Trim());
            }
            packages = directives.GetPackageReferences();
        }

        targetFramework ??= DefaultTargetFramework;

        var sdk = directives.GetSdk();
        var additionalFrameworks = new List<string>();
        if (sdk == WebSdk)
            additionalFrameworks.Add("Microsoft.AspNetCore.App");
        if (projectAssets != null)
            additionalFrameworks.AddRange(projectAssets.FrameworkReferences);
        var isWeb = additionalFrameworks.Any(f =>
            f.StartsWith("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase));

        // Include SDK, shared frameworks and project outputs in the key so web/non-web contexts
        // and rebuilt project assemblies aren't mixed up
        var contextKey = CompilationContextCache.ComputeKey(
            targetFramework,
            projectAssets?.Packages,
            string.Join("\0", new[] { assetsFilePath ?? sdk ?? "" }
                .Concat(additionalFrameworks.Order(StringComparer.OrdinalIgnoreCase))
                .Concat((projectAssets?.ProjectOutputPaths ?? []).Select(ResultCache.FileFingerprint))));

        var references = _contextCache.GetOrAdd(contextKey, () =>
        {
            var refs = FrameworkResolver.GetFrameworkReferences(targetFramework, additionalFrameworks);
            if (projectAssets != null)
                refs.AddRange(projectAssets.References);
            return refs;
        });

        return new SnippetContext
        {
            References = references,
            TargetFramework = targetFramework,
            Packages = packages,
            IsWeb = isWeb,
        };
    }

    /// <summary>
    /// Opens and resolves a complog/.glocontext once per (path, project, size, mtime) and caches
    /// the whole resolution — references, options and target framework.
    /// </summary>
    private ComplogContext ResolveComplog(string path, string? project, string? targetFramework)
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Compilation context file not found: {path}", path);

        var key = $"complog\0{info.FullName}\0{project}\0{targetFramework}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}";
        return _contextCache.GetOrAdd(key, () =>
        {
            using var resolver = CompilationContextResolverFactory.Open(path);
            var resolution = resolver.Resolve(project, targetFramework);
            return new ComplogContext(
                resolution,
                ComplogPackageInference.InferPackages(resolution.References),
                ComplogPackageInference.ReferencesAspNetCore(resolution.References));
        });
    }

    /// <summary>
    /// For snippets read from stdin, writes the directive lines to a stable temp file named by a
    /// hash of the directive set. The SDK keys its file-based-app artifacts on the file path, so
    /// identical directive sets reuse one restore instead of leaking a new artifacts directory
    /// per snippet. Only the directives are written, so concurrent writers produce identical
    /// content and the snippet's own (possibly intentionally broken) code never affects restore.
    /// </summary>
    private static string WriteDirectiveStub(FileDirectiveResult directives)
    {
        var content = string.Join('\n', directives.DirectiveLines) + "\n\nreturn;\n";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))[..16].ToLowerInvariant();

        var dir = Path.Combine(Path.GetTempPath(), "glosharp", "file-based-apps");
        Directory.CreateDirectory(dir);
        var path = Path.Combine(dir, $"glosharp-{hash}.cs");

        if (!File.Exists(path) || File.ReadAllText(path) != content)
        {
            var temp = path + "." + Guid.NewGuid().ToString("N")[..8] + ".tmp";
            File.WriteAllText(temp, content);
            File.Move(temp, path, overwrite: true);
        }

        return path;
    }

    private sealed record BuiltCompilation(
        CSharpCompilation Compilation,
        SyntaxTree Tree,
        string GlobalUsings,
        LanguageVersion LangVersion,
        NullableContextOptions Nullable);

    private static BuiltCompilation CreateCompilation(
        Snippet snippet,
        GloSharpProcessorOptions options,
        SnippetContext context,
        RequestedSettings requested)
    {
        // Global usings: config replaces the defaults; web projects/SDK add the Web SDK set
        IEnumerable<string> usings = options.ImplicitUsings ?? DefaultGlobalUsings;
        if (options.ImplicitUsings == null && context.IsWeb)
            usings = usings.Concat(WebSdkGlobalUsings);

        var globalUsings = string.Join('\n', usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .Select(u => $"global using {u};"));

        LanguageVersion langVersion;
        NullableContextOptions nullable;
        CSharpParseOptions parseOptions;
        CSharpCompilationOptions compilationOptions;

        if (context.Complog != null)
        {
            // Start from the project's own options (AllowUnsafe, defines, NoWarn, warning level, ...)
            // and override only what a snippet needs to differ on.
            var complog = context.Complog;
            langVersion = requested.LangVersion ?? complog.ParseOptions.SpecifiedLanguageVersion;
            nullable = requested.Nullable ?? complog.CompilationOptions.NullableContextOptions;

            parseOptions = complog.ParseOptions
                .WithLanguageVersion(langVersion)
                .WithKind(SourceCodeKind.Regular);
            if (parseOptions.DocumentationMode == DocumentationMode.None)
                parseOptions = parseOptions.WithDocumentationMode(DocumentationMode.Parse);

            compilationOptions = complog.CompilationOptions
                .WithOutputKind(OutputKind.ConsoleApplication)
                .WithMainTypeName(null)
                .WithNullableContextOptions(nullable)
                .WithCryptoKeyFile(null)
                .WithCryptoKeyContainer(null)
                .WithDelaySign(null)
                .WithPublicSign(false);
        }
        else
        {
            langVersion = requested.LangVersion ?? LanguageVersion.Latest;
            nullable = requested.Nullable ?? NullableContextOptions.Enable;

            parseOptions = new CSharpParseOptions(
                langVersion,
                preprocessorSymbols: TargetFrameworkSymbols.Get(context.TargetFramework));
            compilationOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(nullable);
        }

        var globalUsingsTree = CSharpSyntaxTree.ParseText(globalUsings, parseOptions, path: GlobalUsingsPath);
        var tree = CSharpSyntaxTree.ParseText(snippet.Markers.CompilationCode, parseOptions);

        var compilation = CSharpCompilation.Create(
            "GloSharpSnippet",
            [tree, globalUsingsTree],
            context.References,
            compilationOptions);

        return new BuiltCompilation(compilation, tree, globalUsings, langVersion, nullable);
    }

    /// <summary>
    /// Builds the compilation for a result that did not need one up front (cache hit, invalid
    /// options). Invalid lang/nullable values fall back to the defaults; resolution warnings are
    /// dropped (they are already part of the result).
    /// </summary>
    private (CSharpCompilation, SyntaxTree) BuildCompilationIgnoringErrors(Snippet snippet, GloSharpProcessorOptions options)
    {
        var requested = ResolveRequestedSettings(snippet, options);
        var context = ResolveContext(snippet, options, new List<string>());
        var built = CreateCompilation(snippet, options, context, requested);
        return (built.Compilation, built.Tree);
    }

    // ------------------------------------------------------------------
    // Extraction
    // ------------------------------------------------------------------

    private async Task<GloSharpResult> BuildResultAsync(
        Snippet snippet,
        GloSharpProcessorOptions options,
        SnippetContext context,
        BuiltCompilation built,
        List<string> warnings)
    {
        var markers = snippet.Markers;
        var model = built.Compilation.GetSemanticModel(built.Tree);
        var lines = new LineIndex(markers.CompilationCode);

        var hovers = ExtractHovers(snippet, built.Tree, model, lines, warnings);
        var (errors, hiddenErrors, compileSucceeded) = ExtractDiagnostics(snippet, built.Tree, model, lines);

        var completions = markers.CompletionQueries.Count > 0
            ? await ExtractCompletionsAsync(snippet, built, context, lines, warnings)
            : [];

        var processedLines = markers.ProcessedCode.Split('\n');
        var highlights = markers.Highlights
            .Where(h => h.TargetOriginalLine >= 0 && h.TargetOriginalLine < processedLines.Length)
            .Select(h => new GloSharpHighlight
            {
                Line = h.TargetOriginalLine,
                Character = 0,
                Length = processedLines[h.TargetOriginalLine].Length,
                Kind = h.Kind,
            })
            .ToList();

        var tags = markers.Tags
            .Where(t => t.TargetOriginalLine >= 0 && t.TargetOriginalLine < processedLines.Length)
            .Select(t => new GloSharpTag
            {
                Name = t.Name,
                Text = t.Text,
                Line = t.TargetOriginalLine,
            })
            .ToList();

        var hidden = markers.HiddenRanges
            .Select(r => new GloSharpHiddenRange
            {
                Line = CountVisibleLinesBefore(markers.LineMap, r.StartLine),
                SourceStartLine = snippet.ToSourceLine(r.StartLine),
                SourceEndLine = snippet.ToSourceLine(r.EndLine),
            })
            .ToList();

        return new GloSharpResult
        {
            Code = markers.ProcessedCode,
            Original = snippet.Source,
            Hovers = hovers,
            Errors = errors,
            HiddenErrors = hiddenErrors,
            Completions = completions,
            Highlights = highlights,
            Tags = tags,
            Hidden = hidden,
            Meta = new GloSharpMeta
            {
                TargetFramework = context.TargetFramework,
                Packages = context.Packages,
                CompileSucceeded = compileSucceeded,
                Sdk = snippet.Directives.GetSdk(),
                LangVersion = CompilationOptionsMapper.ToDisplayString(built.LangVersion),
                Nullable = CompilationOptionsMapper.ToDisplayString(built.Nullable),
                Complog = options.ComplogPath,
                Warnings = warnings,
            },
        };
    }

    private static int CountVisibleLinesBefore(int[] lineMap, int inputLine)
    {
        var index = Array.BinarySearch(lineMap, inputLine);
        return index >= 0 ? index : ~index;
    }

    // ---- Completions --------------------------------------------------

    private static async Task<List<GloSharpCompletion>> ExtractCompletionsAsync(
        Snippet snippet,
        BuiltCompilation built,
        SnippetContext context,
        LineIndex lines,
        List<string> warnings)
    {
        var markers = snippet.Markers;
        var completions = new List<GloSharpCompletion>();
        var compilationCode = markers.CompilationCode;

        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        using var workspace = new AdhocWorkspace(host);
        var project = workspace.AddProject("GloSharpCompletion", LanguageNames.CSharp)
            .WithCompilationOptions(built.Compilation.Options)
            .WithParseOptions(built.Tree.Options)
            .AddMetadataReferences(context.References);
        project = project.AddDocument(GlobalUsingsPath, built.GlobalUsings).Project;
        var document = project.AddDocument("snippet.cs", compilationCode);
        workspace.TryApplyChanges(document.Project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var completionService = CompletionService.GetService(document);
        if (completionService == null) return completions;

        foreach (var query in markers.CompletionQueries)
        {
            var sourceLine = snippet.ProcessedToSourceLine(query.OriginalLine);
            var markerLine = snippet.ToSourceLine(query.MarkerInputLine);
            var compLine = markers.InputLineToCompilation[markers.LineMap[query.OriginalLine]];

            // A completion caret may sit just after the last character (e.g. after "Console.")
            if (query.Column > lines.GetLineContentLength(compLine))
            {
                warnings.Add(
                    $"Line {markerLine + 1}: the ^| marker points at column {query.Column + 1}, past the end of line {sourceLine + 1}; the completion request was skipped.");
                continue;
            }

            var position = lines.GetLineStart(compLine) + query.Column;
            var completionList = await completionService.GetCompletionsAsync(document, position);

            var items = new List<GloSharpCompletionItem>();
            if (completionList != null)
            {
                // Filter by the identifier already typed before the caret, as an editor would
                var span = completionList.Span;
                var prefix = span.Start <= position && span.Start >= 0
                    ? compilationCode[span.Start..position]
                    : "";

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in completionList.ItemsList)
                {
                    if (prefix.Length > 0 && !item.FilterText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Overloads and generic/non-generic variants share a label: list it once
                    if (!seen.Add(item.DisplayText))
                        continue;

                    items.Add(new GloSharpCompletionItem
                    {
                        Label = item.DisplayText,
                        Kind = item.Tags.FirstOrDefault() ?? "Unknown",
                        Detail = item.InlineDescription.Length > 0 ? item.InlineDescription : null,
                    });
                }
            }

            if (items.Count == 0)
            {
                warnings.Add(
                    $"Line {markerLine + 1}: the ^| marker (line {sourceLine + 1}, column {query.Column + 1}) produced no completions.");
            }

            completions.Add(new GloSharpCompletion
            {
                Line = query.OriginalLine,
                Character = query.Column,
                Items = items,
            });
        }

        return completions;
    }

    // ---- Hovers -------------------------------------------------------

    private static List<GloSharpHover> ExtractHovers(
        Snippet snippet,
        SyntaxTree tree,
        SemanticModel model,
        LineIndex lines,
        List<string> warnings)
    {
        var persistentHovers = ExtractPersistentHovers(snippet, tree, model, lines, warnings);
        var autoHovers = ExtractAutoHovers(snippet, tree, model, lines, persistentHovers);

        var merged = new List<GloSharpHover>(persistentHovers.Count + autoHovers.Count);
        merged.AddRange(persistentHovers);
        merged.AddRange(autoHovers);
        return merged;
    }

    private static List<GloSharpHover> ExtractPersistentHovers(
        Snippet snippet,
        SyntaxTree tree,
        SemanticModel model,
        LineIndex lines,
        List<string> warnings)
    {
        var markers = snippet.Markers;
        var hovers = new List<GloSharpHover>();
        var root = tree.GetCompilationUnitRoot();

        foreach (var query in markers.HoverQueries)
        {
            var sourceLine = snippet.ProcessedToSourceLine(query.OriginalLine);
            var markerLine = snippet.ToSourceLine(query.MarkerInputLine);
            var compLine = markers.InputLineToCompilation[markers.LineMap[query.OriginalLine]];

            if (query.Column >= lines.GetLineContentLength(compLine))
            {
                warnings.Add(lines.GetLineContentLength(compLine) == 0
                    ? $"Line {markerLine + 1}: the ^? marker points at line {sourceLine + 1}, which is empty; the hover was skipped."
                    : $"Line {markerLine + 1}: the ^? marker points at column {query.Column + 1}, past the end of line {sourceLine + 1}; the hover was skipped.");
                continue;
            }

            var position = lines.GetLineStart(compLine) + query.Column;
            var token = root.FindToken(position);
            GloSharpHover? hover = null;
            if (token.Span.Contains(position))
            {
                var character = token.SpanStart - lines.GetLineStart(lines.GetLine(token.SpanStart));
                hover = BuildHover(token, model, query.OriginalLine, character, persistent: true);
            }

            if (hover == null)
            {
                warnings.Add(
                    $"Line {markerLine + 1}: the ^? marker (line {sourceLine + 1}, column {query.Column + 1}) does not point at a symbol; the hover was skipped.");
                continue;
            }

            hovers.Add(hover);
        }

        return hovers;
    }

    private static List<GloSharpHover> ExtractAutoHovers(
        Snippet snippet,
        SyntaxTree tree,
        SemanticModel model,
        LineIndex lines,
        List<GloSharpHover> persistentHovers)
    {
        var markers = snippet.Markers;
        var hovers = new List<GloSharpHover>();
        var root = tree.GetCompilationUnitRoot();

        var persistentPositions = new HashSet<(int Line, int Character)>();
        foreach (var ph in persistentHovers)
            persistentPositions.Add((ph.Line, ph.Character));

        foreach (var token in root.DescendantTokens())
        {
            if (!IsHoverableToken(token))
                continue;

            var (compLine, character) = lines.GetPosition(token.SpanStart);
            var processedLine = markers.InputLineToProcessed[markers.CompilationLineMap[compLine]];
            if (processedLine < 0)
                continue; // hidden code

            if (persistentPositions.Contains((processedLine, character)))
                continue;

            var hover = BuildHover(token, model, processedLine, character, persistent: false);
            if (hover != null)
                hovers.Add(hover);
        }

        return hovers;
    }

    /// <summary>
    /// Allow-list of tokens that carry their own symbol: identifiers (including contextual
    /// keywords such as <c>var</c>), predefined type keywords, <c>this</c>/<c>base</c> and the
    /// <c>new</c> of target-typed/anonymous object creation. Operators and punctuation never
    /// borrow the hover of an enclosing call or declaration.
    /// </summary>
    private static bool IsHoverableToken(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.IdentifierToken))
            return true;

        return token.IsKeyword() && token.Parent is PredefinedTypeSyntax
            or ThisExpressionSyntax
            or BaseExpressionSyntax
            or ImplicitObjectCreationExpressionSyntax
            or AnonymousObjectCreationExpressionSyntax;
    }

    private static ISymbol? ResolveTokenSymbol(SyntaxToken token, SemanticModel model)
    {
        var node = token.Parent;
        if (node == null) return null;

        // 'this' / 'base' show the type they refer to
        if (node is ThisExpressionSyntax or BaseExpressionSyntax)
            return model.GetTypeInfo(node).Type;

        var info = model.GetSymbolInfo(node);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        symbol ??= model.GetDeclaredSymbol(node);

        // Names that label something declared by their parent: anonymous type members
        // (new { Name = x }), named tuple elements ((Name: x, ...)), named arguments.
        if (symbol == null && node.Parent is NameEqualsSyntax or NameColonSyntax)
            symbol = model.GetDeclaredSymbol(node.Parent.Parent!);

        return symbol;
    }

    private static GloSharpHover? BuildHover(SyntaxToken token, SemanticModel model, int line, int character, bool persistent)
    {
        if (!IsHoverableToken(token))
            return null;

        var symbol = ResolveTokenSymbol(token, model);
        if (symbol == null) return null;

        // The synthetic top-level-statements entry point is an implementation detail
        if (symbol is IMethodSymbol { Name: "<Main>$" })
            return null;

        // Unresolvable types would produce a misleading, info-free hover
        if (HasErrorType(symbol))
            return null;

        List<GloSharpDisplayPart> displayParts;
        string text;
        List<GloSharpTypeAnnotation>? typeAnnotations = null;

        if (symbol is IRangeVariableSymbol rangeVariable)
        {
            (displayParts, text) = BuildRangeVariableDisplay(rangeVariable, token, model);
        }
        else
        {
            var parts = symbol.ToDisplayParts(DisplayFormat);
            var prefix = GetSymbolPrefix(symbol);

            var formatter = AnonymousTypeFormatter.FindAnonymousType(symbol) != null ? new AnonymousTypeFormatter() : null;

            displayParts = PrefixParts(prefix);
            if (formatter != null)
            {
                displayParts.AddRange(formatter.TransformDisplayParts(parts, symbol));
            }
            else
            {
                foreach (var part in parts)
                {
                    displayParts.Add(new GloSharpDisplayPart
                    {
                        Kind = SymbolDisplayPartKindMapping.ToJsonKind(part.Kind),
                        Text = part.ToString(),
                    });
                }
            }

            var rawDisplayString = symbol.ToDisplayString(DisplayFormat);
            var display = formatter != null ? formatter.TransformDisplayString(rawDisplayString) : rawDisplayString;
            text = prefix != null ? $"({prefix}) {display}" : display;
            typeAnnotations = formatter?.GetAnnotations();
        }

        int? overloadCount = null;
        if (symbol is IMethodSymbol method && method.ContainingType != null)
        {
            var overloads = method.ContainingType.GetMembers(method.Name)
                .OfType<IMethodSymbol>()
                .Count();
            if (overloads > 1)
            {
                overloadCount = overloads;
                text += overloads == 2 ? " (+ 1 overload)" : $" (+ {overloads - 1} overloads)";
            }
        }

        var docs = DocCommentFormatter.Extract(symbol, model.Compilation);

        return new GloSharpHover
        {
            Line = line,
            Character = character,
            Length = token.Text.Length,
            Text = text,
            Parts = displayParts,
            Docs = docs,
            SymbolKind = SymbolDisplayPartKindMapping.ToSymbolKindString(symbol),
            TargetText = token.Text,
            OverloadCount = overloadCount,
            TypeAnnotations = typeAnnotations,
            Persistent = persistent,
        };
    }

    private static List<GloSharpDisplayPart> PrefixParts(string? prefix)
    {
        var parts = new List<GloSharpDisplayPart>();
        if (prefix != null)
        {
            parts.Add(new GloSharpDisplayPart { Kind = "punctuation", Text = "(" });
            parts.Add(new GloSharpDisplayPart { Kind = "text", Text = prefix });
            parts.Add(new GloSharpDisplayPart { Kind = "punctuation", Text = ")" });
            parts.Add(new GloSharpDisplayPart { Kind = "space", Text = " " });
        }
        return parts;
    }

    /// <summary>
    /// Range variables (<c>from p in people</c>) have no type in their symbol; Roslyn's display
    /// renders them as "? p". Show "(range variable) T p" like Visual Studio does instead.
    /// </summary>
    private static (List<GloSharpDisplayPart> Parts, string Text) BuildRangeVariableDisplay(
        IRangeVariableSymbol rangeVariable, SyntaxToken token, SemanticModel model)
    {
        var type = GetRangeVariableType(rangeVariable, token, model);
        var parts = PrefixParts("range variable");
        var text = new StringBuilder("(range variable) ");

        if (type != null)
        {
            foreach (var part in type.ToDisplayParts(DisplayFormat))
            {
                parts.Add(new GloSharpDisplayPart
                {
                    Kind = SymbolDisplayPartKindMapping.ToJsonKind(part.Kind),
                    Text = part.ToString(),
                });
            }
            parts.Add(new GloSharpDisplayPart { Kind = "space", Text = " " });
            text.Append(type.ToDisplayString(DisplayFormat)).Append(' ');
        }

        parts.Add(new GloSharpDisplayPart { Kind = "localName", Text = rangeVariable.Name });
        text.Append(rangeVariable.Name);
        return (parts, text.ToString());
    }

    private static ITypeSymbol? GetRangeVariableType(IRangeVariableSymbol rangeVariable, SyntaxToken token, SemanticModel model)
    {
        // A usage binds directly
        if (token.Parent is IdentifierNameSyntax usage)
        {
            var usageType = model.GetTypeInfo(usage).Type;
            if (usageType is { TypeKind: not TypeKind.Error })
                return usageType;
        }

        // Declaration: find a usage in the same query
        var query = token.Parent?.FirstAncestorOrSelf<QueryExpressionSyntax>();
        if (query != null)
        {
            foreach (var identifier in query.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Identifier.ValueText != rangeVariable.Name) continue;
                if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, rangeVariable)) continue;
                var type = model.GetTypeInfo(identifier).Type;
                if (type is { TypeKind: not TypeKind.Error })
                    return type;
            }
        }

        // Unused: derive from the declaring clause
        return token.Parent switch
        {
            FromClauseSyntax { Type: { } explicitType } => model.GetTypeInfo(explicitType).Type,
            FromClauseSyntax from => GetElementType(model.GetTypeInfo(from.Expression).Type),
            JoinClauseSyntax { Type: { } explicitType } => model.GetTypeInfo(explicitType).Type,
            JoinClauseSyntax join => GetElementType(model.GetTypeInfo(join.InExpression).Type),
            LetClauseSyntax let => model.GetTypeInfo(let.Expression).Type,
            _ => null,
        };
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol? type)
    {
        if (type == null) return null;
        if (type is IArrayTypeSymbol array) return array.ElementType;

        var enumerable = type.AllInterfaces
            .Prepend(type as INamedTypeSymbol)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        return enumerable?.TypeArguments[0];
    }

    // ---- Diagnostics --------------------------------------------------

    private static (List<GloSharpError> Errors, List<GloSharpError> HiddenErrors, bool CompileSucceeded) ExtractDiagnostics(
        Snippet snippet,
        SyntaxTree tree,
        SemanticModel model,
        LineIndex lines)
    {
        var markers = snippet.Markers;
        var errors = new List<GloSharpError>();
        var hiddenErrors = new List<GloSharpError>();
        var hasUnexpectedErrors = false;

        // @errors expectations, keyed by the input line they target (which may be hidden)
        var expectations = markers.ErrorExpectations
            .GroupBy(e => e.InputLine)
            .ToDictionary(g => g.Key, g => g.SelectMany(e => e.Codes).ToHashSet(StringComparer.Ordinal));
        var matched = new HashSet<(int InputLine, string Code)>();

        bool IsSuppressed(string code) =>
            markers.SuppressAllErrors || markers.SuppressedErrorCodes.Contains(code);

        var diagnostics = model.GetDiagnostics()
            .Where(d => d.Severity >= DiagnosticSeverity.Info)
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree)
            .OrderBy(d => d.Location.SourceSpan.Start);

        foreach (var diagnostic in diagnostics)
        {
            var span = diagnostic.Location.SourceSpan;
            var (compLine, character) = lines.GetPosition(span.Start);
            var inputLine = markers.CompilationLineMap[compLine];
            var processedLine = markers.InputLineToProcessed[inputLine];
            var code = diagnostic.Id;

            // Expectations are matched before suppression so a suppressed diagnostic
            // still satisfies its @errors line.
            var expected = expectations.TryGetValue(inputLine, out var expectedCodes) && expectedCodes.Contains(code);
            if (expected)
                matched.Add((inputLine, code));

            // Block-level suppression (@noErrors / @suppressErrors) hides errors, warnings and info
            if (IsSuppressed(code))
                continue;

            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                _ => "hidden",
            };

            var sourceLine = snippet.ToSourceLine(inputLine);

            if (processedLine < 0)
            {
                // Hidden (cut/region) code: errors still fail the snippet, but can't be placed
                // in the rendered code. Warnings/info in setup code are not reported.
                if (severity == "error")
                {
                    if (!expected) hasUnexpectedErrors = true;
                    hiddenErrors.Add(new GloSharpError
                    {
                        Line = -1,
                        Character = character,
                        Length = Math.Max(1, span.Length),
                        Code = code,
                        Message = diagnostic.GetMessage(),
                        Severity = severity,
                        Expected = expected,
                        SourceLine = sourceLine,
                        SourceCharacter = character,
                    });
                }
                continue;
            }

            if (severity == "error" && !expected)
                hasUnexpectedErrors = true;

            int? endLine = null;
            int? endCharacter = null;
            var (endCompLine, endChar) = lines.GetPosition(span.End);
            if (endCompLine != compLine)
            {
                var processedEndLine = markers.InputLineToProcessed[markers.CompilationLineMap[endCompLine]];
                if (processedEndLine >= 0)
                {
                    endLine = processedEndLine;
                    endCharacter = endChar;
                }
            }

            errors.Add(new GloSharpError
            {
                Line = processedLine,
                Character = character,
                Length = Math.Max(1, span.Length),
                EndLine = endLine,
                EndCharacter = endCharacter,
                Code = code,
                Message = diagnostic.GetMessage(),
                Severity = severity,
                Expected = expected,
                SourceLine = sourceLine,
                SourceCharacter = character,
            });
        }

        // @errors expectations that nothing satisfied: the snippet no longer demonstrates the
        // error it documents.
        foreach (var expectation in markers.ErrorExpectations)
        {
            foreach (var code in expectation.Codes)
            {
                if (matched.Contains((expectation.InputLine, code)) || IsSuppressed(code))
                    continue;
                matched.Add((expectation.InputLine, code)); // report each (line, code) once

                hasUnexpectedErrors = true;
                var compLine = markers.InputLineToCompilation[expectation.InputLine];
                var lineText = compLine >= 0 ? lines.GetLineText(compLine) : "";
                var indent = lineText.Length - lineText.TrimStart().Length;

                var error = new GloSharpError
                {
                    Line = expectation.OriginalLine,
                    Character = indent,
                    Length = Math.Max(1, lineText.Trim().Length),
                    Code = GloSharpDiagnosticCodes.UnmatchedExpectedError,
                    Message = $"Expected error {code} (from '// @errors') was not reported on this line.",
                    Severity = "error",
                    Expected = false,
                    SourceLine = snippet.ToSourceLine(expectation.InputLine),
                    SourceCharacter = indent,
                };

                if (expectation.OriginalLine >= 0)
                    errors.Add(error);
                else
                    hiddenErrors.Add(error);
            }
        }

        return (errors, hiddenErrors, !hasUnexpectedErrors);
    }

    private static bool HasErrorType(ISymbol symbol) => symbol switch
    {
        ILocalSymbol { Type.TypeKind: TypeKind.Error } => true,
        IParameterSymbol { Type.TypeKind: TypeKind.Error } => true,
        IFieldSymbol { Type.TypeKind: TypeKind.Error } => true,
        IPropertySymbol { Type.TypeKind: TypeKind.Error } => true,
        IMethodSymbol { ReturnType.TypeKind: TypeKind.Error } => true,
        IMethodSymbol { ContainingType.TypeKind: TypeKind.Error } => true,
        ITypeSymbol { TypeKind: TypeKind.Error } => true,
        _ => false,
    };

    private static string? GetSymbolPrefix(ISymbol symbol) => symbol switch
    {
        ILocalSymbol => "local variable",
        IParameterSymbol => "parameter",
        IRangeVariableSymbol => "range variable",
        IFieldSymbol f => f.IsConst ? "constant" : "field",
        IPropertySymbol => "property",
        IMethodSymbol m => m.IsExtensionMethod ? "extension" : "method",
        IEventSymbol => "event",
        INamedTypeSymbol nts => nts.TypeKind switch
        {
            TypeKind.Class => nts.IsRecord ? "record" : "class",
            TypeKind.Struct => nts.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => null,
        },
        INamespaceSymbol => "namespace",
        _ => null,
    };
}
