using System.Security.Cryptography;
using System.Text;

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
            var resolveFilePath = sourceFilePath ?? WriteDirectiveStub(directives);
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
}
