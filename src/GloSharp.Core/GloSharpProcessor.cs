using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

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

/// <summary>
/// Compiles a snippet and extracts its hovers, diagnostics, completions and annotations.
/// </summary>
/// <remarks>
/// <para>
/// The pipeline, one stage per type:
/// <see cref="Snippet.Prepare"/> (directives, region, markers) →
/// result cache lookup →
/// <see cref="RequestedSettings"/> (langVersion/nullable) →
/// <see cref="ICompilationContextProvider"/> (references and options) →
/// <see cref="CompilationBuilder"/> →
/// <see cref="HoverExtractor"/>, <see cref="DiagnosticExtractor"/>,
/// <see cref="CompletionExtractor"/>, <see cref="AnnotationExtractor"/>.
/// </para>
/// <para>
/// Thread-safe: one instance may process many snippets concurrently. Reuse an instance (or share
/// a <see cref="CompilationContextCache"/>) so resolved references and complogs are shared.
/// </para>
/// </remarks>
public class GloSharpProcessor
{
    private readonly CompilationContextCache _contextCache;

    public GloSharpProcessor(CompilationContextCache? contextCache = null)
    {
        _contextCache = contextCache ?? new CompilationContextCache();
    }

    public async Task<GloSharpResult> ProcessAsync(string source, GloSharpProcessorOptions? options = null)
    {
        var processResult = await ProcessWithContextAsync(source, options);
        return processResult.Result;
    }

    public async Task<GloSharpProcessResult> ProcessWithContextAsync(string source, GloSharpProcessorOptions? options = null)
    {
        options ??= new GloSharpProcessorOptions();

        // 1. Text-only preprocessing: #: directives, region, markers. Cheap, no Roslyn binding.
        var snippet = Snippet.Prepare(source, options.RegionName);
        var contextProvider = CompilationContextProviders.Select(snippet, options, _contextCache);

        // 2. Result cache: a hit skips context resolution and compilation entirely. The
        //    compilation (needed only for syntax classification when rendering) is built lazily.
        ResultCache? resultCache = null;
        string? resultCacheKey = null;
        if (options.CacheDir != null)
        {
            resultCache = new ResultCache(options.CacheDir);
            resultCacheKey = ComputeResultCacheKey(snippet, options, contextProvider);

            var cached = resultCache.TryGet(resultCacheKey);
            if (cached != null)
            {
                return new GloSharpProcessResult(cached, () => CompileIgnoringErrors(snippet, options, contextProvider))
                {
                    FromCache = true,
                };
            }
        }

        // 3. Requested language version / nullable context (marker > config)
        var requested = RequestedSettings.Resolve(snippet, options.LangVersion, options.Nullable);
        if (requested.Errors.Count > 0)
        {
            return new GloSharpProcessResult(
                InvalidSettingsResult(snippet, options, requested),
                () => CompileIgnoringErrors(snippet, options, contextProvider));
        }

        // 4. Resolve references and options, compile
        var context = contextProvider.Resolve();
        var compilation = CompilationBuilder.Build(snippet, context, requested, options.ImplicitUsings);

        // 5. Extract
        var result = await ExtractAsync(snippet, options, compilation);

        resultCache?.Set(resultCacheKey!, result);

        return new GloSharpProcessResult(result, compilation.Compilation, compilation.Tree);
    }

    /// <summary>
    /// The result-cache key: the effective options, the snippet source and fingerprints of the
    /// on-disk inputs the context provider reads by path.
    /// </summary>
    private static string ComputeResultCacheKey(
        Snippet snippet,
        GloSharpProcessorOptions options,
        ICompilationContextProvider contextProvider)
    {
        // The source path only matters for file-based apps (#: directives are resolved relative to it)
        var keyOptions = snippet.Directives.HasDirectives ? options : options with { SourceFilePath = null };
        return ResultCache.ComputeKey(snippet.Source, keyOptions, contextProvider.GetCacheFingerprints());
    }

    /// <summary>
    /// Builds the compilation for a result that did not need one up front (cache hit, invalid
    /// options). Invalid lang/nullable values fall back to the defaults; resolution warnings are
    /// dropped (they are already part of the result).
    /// </summary>
    private static (CSharpCompilation, SyntaxTree) CompileIgnoringErrors(
        Snippet snippet,
        GloSharpProcessorOptions options,
        ICompilationContextProvider contextProvider)
    {
        var requested = RequestedSettings.Resolve(snippet, options.LangVersion, options.Nullable);
        var compilation = CompilationBuilder.Build(snippet, contextProvider.Resolve(), requested, options.ImplicitUsings);
        return (compilation.Compilation, compilation.Tree);
    }

    /// <summary>The result for invalid langVersion/nullable values: just the GS0001/GS0002 errors.</summary>
    private static GloSharpResult InvalidSettingsResult(
        Snippet snippet,
        GloSharpProcessorOptions options,
        RequestedSettings requested) => new()
    {
        Code = snippet.Markers.ProcessedCode,
        Original = snippet.Source,
        Hovers = [],
        Errors = requested.Errors,
        Meta = new GloSharpMeta
        {
            TargetFramework = options.TargetFramework
                ?? snippet.Directives.GetProperty("TargetFramework")
                ?? FrameworkResolver.DefaultTargetFramework,
            CompileSucceeded = false,
            Sdk = snippet.Sdk,
            LangVersion = snippet.Markers.LangVersion ?? options.LangVersion,
            Nullable = snippet.Markers.Nullable ?? options.Nullable,
            Complog = options.ComplogPath,
        },
    };

    private static async Task<GloSharpResult> ExtractAsync(
        Snippet snippet,
        GloSharpProcessorOptions options,
        SnippetCompilation compilation)
    {
        var context = compilation.Context;
        var model = compilation.Compilation.GetSemanticModel(compilation.Tree);

        // Resolution warnings first, then the extractors' (caret and completion problems)
        var warnings = new List<string>(context.Warnings);

        var hovers = HoverExtractor.Extract(snippet, model, warnings);
        var diagnostics = DiagnosticExtractor.Extract(snippet, model);
        var completions = await CompletionExtractor.ExtractAsync(snippet, compilation, warnings);

        return new GloSharpResult
        {
            Code = snippet.Markers.ProcessedCode,
            Original = snippet.Source,
            Hovers = hovers,
            Errors = diagnostics.Errors,
            HiddenErrors = diagnostics.HiddenErrors,
            Completions = completions,
            Highlights = AnnotationExtractor.GetHighlights(snippet),
            Tags = AnnotationExtractor.GetTags(snippet),
            Hidden = AnnotationExtractor.GetHiddenRanges(snippet),
            Meta = new GloSharpMeta
            {
                TargetFramework = context.TargetFramework,
                Packages = context.Packages,
                CompileSucceeded = diagnostics.CompileSucceeded,
                Sdk = snippet.Sdk,
                LangVersion = CompilationOptionsMapper.ToDisplayString(compilation.LangVersion),
                Nullable = CompilationOptionsMapper.ToDisplayString(compilation.Nullable),
                Complog = options.ComplogPath,
                Warnings = warnings,
            },
        };
    }
}
