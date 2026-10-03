namespace GloSharp.Core;

/// <summary>
/// A project's restored packages, project references and own build output
/// (<c>obj/project.assets.json</c>), on top of the framework references.
/// </summary>
internal sealed class ProjectAssetsContextProvider(
    Snippet snippet,
    string projectPath,
    string? targetFramework,
    CompilationContextCache cache)
    : FrameworkReferencesContextProvider(snippet, cache)
{
    // The assets file is parsed once per snippet even when both the cache key and the context
    // need it.
    private string? _assetsFilePath;
    private ProjectAssetsResult? _assets;

    public override IEnumerable<string> GetCacheFingerprints()
    {
        string assetsFilePath;
        try { assetsFilePath = FindAssetsFile(); }
        catch (FileNotFoundException) { assetsFilePath = Path.Combine(projectPath, "obj", "project.assets.json"); }

        var fingerprints = new List<string> { "assets:" + ResultCache.FileFingerprint(assetsFilePath) };

        // The project's own built assembly (and its ProjectReferences') change hovers too
        try
        {
            fingerprints.AddRange(ResolveProjectAssets().ProjectOutputPaths
                .Select(o => "output:" + ResultCache.FileFingerprint(o)));
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.Text.Json.JsonException) { }

        return fingerprints;
    }

    protected override ResolvedAssets ResolveAssets()
    {
        var assets = ResolveProjectAssets();
        return new ResolvedAssets(
            assets,
            _assetsFilePath,
            targetFramework ?? assets.TargetFramework,
            assets.Packages,
            assets.Warnings);
    }

    private string FindAssetsFile() =>
        _assetsFilePath ??= ProjectAssetsResolver.FindAssetsFile(projectPath);

    private ProjectAssetsResult ResolveProjectAssets() =>
        _assets ??= ProjectAssetsResolver.Resolve(FindAssetsFile(), targetFramework);
}
