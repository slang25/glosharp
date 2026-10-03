using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>
/// Base for the contexts compiled against this machine's targeting packs: the framework alone,
/// or the framework plus a restored project's / file-based app's package and project references.
/// Subclasses say where the assets come from; this class picks the target framework, adds the
/// shared frameworks (ASP.NET Core for the Web SDK / a <c>FrameworkReference</c>) and caches the
/// reference list.
/// </summary>
internal abstract class FrameworkReferencesContextProvider : ICompilationContextProvider
{
    private const string WebSdk = "Microsoft.NET.Sdk.Web";

    private readonly CompilationContextCache _cache;

    protected FrameworkReferencesContextProvider(Snippet snippet, CompilationContextCache cache)
    {
        Snippet = snippet;
        _cache = cache;
    }

    protected Snippet Snippet { get; }

    /// <summary>What a subclass resolved before the framework references are added.</summary>
    /// <param name="Assets">Restored assets, or null for the framework alone.</param>
    /// <param name="AssetsFilePath">The project's assets file (project mode only; part of the reference cache key).</param>
    /// <param name="TargetFramework">The target framework, or null for <see cref="FrameworkResolver.DefaultTargetFramework"/>.</param>
    /// <param name="Packages">What <c>meta.packages</c> reports.</param>
    /// <param name="Warnings">Resolution warnings.</param>
    protected sealed record ResolvedAssets(
        ProjectAssetsResult? Assets,
        string? AssetsFilePath,
        string? TargetFramework,
        List<PackageReference> Packages,
        List<string> Warnings);

    protected abstract ResolvedAssets ResolveAssets();

    public virtual IEnumerable<string> GetCacheFingerprints() => [];

    public CompilationContext Resolve()
    {
        var resolved = ResolveAssets();
        var assets = resolved.Assets;
        var targetFramework = resolved.TargetFramework ?? FrameworkResolver.DefaultTargetFramework;

        var sdk = Snippet.Sdk;
        var additionalFrameworks = new List<string>();
        if (sdk == WebSdk)
            additionalFrameworks.Add("Microsoft.AspNetCore.App");
        if (assets != null)
            additionalFrameworks.AddRange(assets.FrameworkReferences);
        var isWeb = additionalFrameworks.Any(f =>
            f.StartsWith("Microsoft.AspNetCore.App", StringComparison.OrdinalIgnoreCase));

        // Include SDK, shared frameworks and project outputs in the key so web/non-web contexts
        // and rebuilt project assemblies aren't mixed up
        var contextKey = CompilationContextCache.ComputeKey(
            targetFramework,
            assets?.Packages,
            string.Join("\0", new[] { resolved.AssetsFilePath ?? sdk ?? "" }
                .Concat(additionalFrameworks.Order(StringComparer.OrdinalIgnoreCase))
                .Concat((assets?.ProjectOutputPaths ?? []).Select(ResultCache.FileFingerprint))));

        var references = _cache.GetOrAdd(contextKey, () =>
        {
            var refs = FrameworkResolver.GetFrameworkReferences(targetFramework, additionalFrameworks);
            if (assets != null)
                refs.AddRange(assets.References);
            return refs;
        });

        return new CompilationContext
        {
            References = references,
            TargetFramework = targetFramework,
            Packages = resolved.Packages,
            IsWeb = isWeb,
            ParseOptions = new CSharpParseOptions(
                LanguageVersion.Latest,
                preprocessorSymbols: TargetFrameworkSymbols.Get(targetFramework)),
            CompilationOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable),
            Warnings = resolved.Warnings,
        };
    }
}

/// <summary>The framework's reference assemblies only (no project, complog or <c>#:</c> directives).</summary>
internal sealed class FrameworkContextProvider(Snippet snippet, string? targetFramework, CompilationContextCache cache)
    : FrameworkReferencesContextProvider(snippet, cache)
{
    protected override ResolvedAssets ResolveAssets() =>
        new(Assets: null, AssetsFilePath: null, targetFramework, Packages: [], Warnings: []);
}
