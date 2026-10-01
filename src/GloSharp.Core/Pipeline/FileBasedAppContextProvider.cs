namespace GloSharp.Core;

/// <summary>
/// A file-based app: the SDK restores the snippet's <c>#:package</c> / <c>#:sdk</c> /
/// <c>#:property</c> directives. If that fails the snippet is compiled against the framework
/// alone, with a warning.
/// </summary>
internal sealed class FileBasedAppContextProvider(
    Snippet snippet,
    string? sourceFilePath,
    string? targetFramework,
    bool noRestore,
    CompilationContextCache cache)
    : FrameworkReferencesContextProvider(snippet, cache)
{
    protected override ResolvedAssets ResolveAssets()
    {
        var directives = Snippet.Directives;
        var tfmProperty = directives.GetProperty("TargetFramework");
        var warnings = new List<string>();
        ProjectAssetsResult? assets = null;
        string? tfm;

        try
        {
            // Snippets without a file (stdin) restore a stand-in holding just their directives
            var resolveFilePath = sourceFilePath ?? FileBasedAppResolver.WriteDirectivesFile(directives.DirectiveLines);
            assets = FileBasedAppResolver.ResolveReferences(resolveFilePath, targetFramework, noRestore);
            tfm = targetFramework ?? tfmProperty ?? assets.TargetFramework;
            warnings.AddRange(assets.Warnings);
        }
        catch (Exception ex)
        {
            tfm = targetFramework ?? tfmProperty;
            warnings.Add(
                "Could not resolve the file-based app directives (" +
                string.Join(", ", directives.DirectiveLines) +
                "); the snippet was compiled against the framework only. " +
                ex.Message.Trim());
        }

        return new ResolvedAssets(assets, AssetsFilePath: null, tfm, directives.GetPackageReferences(), warnings);
    }
}
