using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <summary>
/// A compilation recorded in a complog or .glocontext: its references and its own options
/// (AllowUnsafe, defines, NoWarn, warning level, ...), adapted so a snippet compiles as a
/// top-level-statements console app. Bypasses project, directive and framework resolution.
/// </summary>
internal sealed class ComplogContextProvider(
    string path,
    string? project,
    string? targetFramework,
    CompilationContextCache cache) : ICompilationContextProvider
{
    public IEnumerable<string> GetCacheFingerprints() => ["complog:" + ResultCache.FileFingerprint(path)];

    /// <summary>
    /// Opens and resolves the file once per (path, project, framework, size, mtime) and caches
    /// the whole context.
    /// </summary>
    public CompilationContext Resolve()
    {
        var info = new FileInfo(path);
        if (!info.Exists)
            throw new FileNotFoundException($"Compilation context file not found: {path}", path);

        var key = $"complog\0{info.FullName}\0{project}\0{targetFramework}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}";
        return cache.GetOrAdd(key, () =>
        {
            using var resolver = CompilationContextResolverFactory.Open(path);
            return ToContext(resolver.Resolve(project, targetFramework));
        });
    }

    internal static CompilationContext ToContext(ComplogResolutionResult resolution)
    {
        // Start from the project's own options and override only what a snippet needs to
        // differ on.
        var parseOptions = resolution.ParseOptions.WithKind(SourceCodeKind.Regular);
        if (parseOptions.DocumentationMode == DocumentationMode.None)
            parseOptions = parseOptions.WithDocumentationMode(DocumentationMode.Parse);

        var compilationOptions = resolution.CompilationOptions
            .WithOutputKind(OutputKind.ConsoleApplication)
            .WithMainTypeName(null)
            .WithCryptoKeyFile(null)
            .WithCryptoKeyContainer(null)
            .WithDelaySign(null)
            .WithPublicSign(false);

        return new CompilationContext
        {
            References = resolution.References,
            TargetFramework = resolution.TargetFramework,
            Packages = ComplogPackageInference.InferPackages(resolution.References),
            IsWeb = ComplogPackageInference.ReferencesAspNetCore(resolution.References),
            ParseOptions = parseOptions,
            CompilationOptions = compilationOptions,
            Warnings = resolution.Warnings,
        };
    }
}
