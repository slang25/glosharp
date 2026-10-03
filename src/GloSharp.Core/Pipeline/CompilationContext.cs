using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>
/// Everything a snippet is compiled against, independent of the snippet's own settings:
/// references, base parse/compilation options (their language version and nullable context are
/// the defaults a snippet can override), the effective target framework and what
/// <c>meta</c> reports about it.
/// </summary>
internal sealed class CompilationContext
{
    public required List<MetadataReference> References { get; init; }

    /// <summary>The effective target framework (<c>meta.targetFramework</c>).</summary>
    public required string TargetFramework { get; init; }

    /// <summary>The packages reported in <c>meta.packages</c>.</summary>
    public required List<PackageReference> Packages { get; init; }

    /// <summary>True when ASP.NET Core is referenced, so the Web SDK's implicit usings apply.</summary>
    public required bool IsWeb { get; init; }

    /// <summary>
    /// Base parse options, including the target framework's preprocessor symbols. Their
    /// <see cref="CSharpParseOptions.SpecifiedLanguageVersion"/> is the default language version.
    /// </summary>
    public required CSharpParseOptions ParseOptions { get; init; }

    /// <summary>
    /// Base compilation options. Their <see cref="CompilationOptions.NullableContextOptions"/> is
    /// the default nullable context.
    /// </summary>
    public required CSharpCompilationOptions CompilationOptions { get; init; }

    /// <summary>Non-fatal resolution problems, reported first in <c>meta.warnings</c>.</summary>
    public IReadOnlyList<string> Warnings { get; init; } = [];
}

/// <summary>
/// One way of obtaining a <see cref="CompilationContext"/> (framework only, a project's assets,
/// a file-based app's restore, a complog/.glocontext). A provider is created per snippet and is
/// not thread-safe; anything worth sharing between snippets goes through the
/// <see cref="CompilationContextCache"/> it was given.
/// </summary>
internal interface ICompilationContextProvider
{
    /// <summary>
    /// Cheap change-detection fingerprints of the on-disk inputs this provider reads by path, for
    /// the result-cache key. Must not restore, build or open archives.
    /// </summary>
    IEnumerable<string> GetCacheFingerprints();

    /// <summary>Resolves the context. May restore, build or open a complog.</summary>
    CompilationContext Resolve();
}

internal static class CompilationContextProviders
{
    /// <summary>
    /// Picks the provider for a snippet: complog/.glocontext, else project, else file-based app
    /// (the snippet has <c>#:</c> directives), else the framework alone.
    /// </summary>
    public static ICompilationContextProvider Select(
        Snippet snippet,
        GloSharpProcessorOptions options,
        CompilationContextCache cache)
    {
        if (options.ComplogPath != null)
            return new ComplogContextProvider(options.ComplogPath, options.ComplogProject, options.TargetFramework, cache);

        if (options.ProjectPath != null)
            return new ProjectAssetsContextProvider(snippet, options.ProjectPath, options.TargetFramework, cache);

        if (snippet.Directives.HasDirectives)
            return new FileBasedAppContextProvider(snippet, options.SourceFilePath, options.TargetFramework, options.NoRestore, cache);

        return new FrameworkContextProvider(snippet, options.TargetFramework, cache);
    }
}
