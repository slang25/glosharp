using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GloSharp.Core;

public class FileBasedAppResolveResult
{
    public required string AssetsFilePath { get; init; }
    public required string TargetFramework { get; init; }
}

/// <summary>
/// Resolves the package references of a .NET 10+ file-based app (<c>#:package</c>,
/// <c>#:sdk</c>, <c>#:property</c>) by having the SDK <em>restore</em> it — never build it — so
/// snippets that intentionally don't compile still get their packages.
/// </summary>
public static class FileBasedAppResolver
{
    private static readonly ConcurrentDictionary<string, Version?> SdkVersionByDirectory = new(StringComparer.Ordinal);

    public static void EnsureSdkVersion(string? workingDirectory = null)
    {
        var version = GetDotnetSdkVersion(workingDirectory);
        if (version == null)
            throw new InvalidOperationException("Could not determine .NET SDK version. Ensure 'dotnet' is on the PATH.");

        if (version.Major < 10)
            throw new InvalidOperationException(
                $".NET 10+ SDK is required for file-based app directives (#:package, #:sdk, etc.), but found .NET {version}. " +
                "Either upgrade the SDK or use --project with a .csproj instead.");
    }

    /// <summary>
    /// The SDK version <c>dotnet</c> selects in <paramref name="workingDirectory"/> (a global.json
    /// there can pin an older SDK). Cached per directory for the life of the process.
    /// </summary>
    public static Version? GetDotnetSdkVersion(string? workingDirectory = null)
    {
        var dir = workingDirectory ?? Directory.GetCurrentDirectory();
        if (SdkVersionByDirectory.TryGetValue(dir, out var cached))
            return cached;

        // Only cache successful probes, so a transient failure isn't remembered for the process
        var probed = Probe(dir);
        if (probed != null)
            SdkVersionByDirectory[dir] = probed;
        return probed;

        static Version? Probe(string d)
        {
            try
            {
                var result = ProcessRunner.Run(
                    FrameworkResolver.GetDotnetExecutable(), ["--version"], d, TimeSpan.FromMinutes(1));
                if (!result.Succeeded) return null;

                // Parse version like "10.0.100" or "11.0.100-preview.1.26104.118"
                var output = result.StandardOutput.Trim();
                var dashIndex = output.IndexOf('-');
                var versionPart = dashIndex >= 0 ? output[..dashIndex] : output;
                return Version.TryParse(versionPart, out var version) ? version : null;
            }
            catch (InvalidOperationException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// Restores the file-based app (unless <paramref name="noRestore"/>) and returns the
    /// location of its project.assets.json and its target framework. Throws with the full
    /// restore output (NuGet errors such as NU1101 are written to stdout) on failure.
    /// </summary>
    public static FileBasedAppResolveResult RestoreAndDiscoverAssets(string sourceFilePath, bool noRestore = false)
    {
        var fullPath = Path.GetFullPath(sourceFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Source file not found: {fullPath}", fullPath);

        var workingDirectory = Path.GetDirectoryName(fullPath)!;
        EnsureSdkVersion(workingDirectory);

        string[] properties = ["--getProperty:ProjectAssetsFile", "--getProperty:TargetFramework"];
        var args = noRestore
            // Evaluation only: with --getProperty and no target, 'build' doesn't compile.
            ? (string[])["build", fullPath, "--no-restore", .. properties]
            : ["restore", fullPath, .. properties];

        var result = ProcessRunner.Run(FrameworkResolver.GetDotnetExecutable(), args, workingDirectory)
            .EnsureSuccess(noRestore
                ? $"Evaluating file-based app '{fullPath}'"
                : $"Restoring packages for file-based app '{fullPath}'");

        var resolved = ParsePropertyOutput(result.StandardOutput, fullPath);
        if (!File.Exists(resolved.AssetsFilePath))
        {
            throw new FileNotFoundException(
                noRestore
                    ? $"File-based app '{fullPath}' has not been restored (no '{resolved.AssetsFilePath}'). " +
                      "Run without --no-restore, or run 'dotnet restore' on the file first."
                    : $"Restore of file-based app '{fullPath}' reported success but produced no '{resolved.AssetsFilePath}'.",
                resolved.AssetsFilePath);
        }

        return resolved;
    }

    /// <summary>Restores the file-based app and resolves its package references.</summary>
    public static ProjectAssetsResult ResolveReferences(string sourceFilePath, string? targetFramework = null, bool noRestore = false)
    {
        var restored = RestoreAndDiscoverAssets(sourceFilePath, noRestore);
        // The virtual project's own output is the snippet itself; never reference it.
        return ProjectAssetsResolver.Resolve(
            restored.AssetsFilePath,
            targetFramework ?? restored.TargetFramework,
            includeProjectOutputs: false);
    }

    /// <summary>
    /// Resolves package references for snippet text that has no file on disk (e.g. stdin).
    /// Only the <c>#:</c> directive lines matter for restore, so they are written to a stable
    /// file named after their hash under the glosharp cache. Snippets with the same directives
    /// share one restore, and the SDK's per-file artifacts directory is reused instead of a new
    /// one being leaked for every snippet.
    /// </summary>
    public static ProjectAssetsResult ResolveReferencesForSource(string source, string? targetFramework = null, bool noRestore = false)
    {
        var path = WriteDirectivesFile(source);
        return ResolveReferences(path, targetFramework, noRestore);
    }

    internal static string WriteDirectivesFile(string source, string? root = null) =>
        WriteDirectivesFile(ExtractDirectiveLines(source), root);

    /// <summary>
    /// Writes <paramref name="directiveLines"/> to <c>&lt;root&gt;/&lt;hash&gt;/snippet.cs</c>, named
    /// by a hash of the directive set (root: <c>GLOSHARP_CACHE_DIR/file-based-apps</c>, else the
    /// local application data folder). Identical sets share one file and therefore one SDK
    /// restore; concurrent writers produce identical content.
    /// </summary>
    internal static string WriteDirectivesFile(IReadOnlyList<string> directiveLines, string? root = null)
    {
        var content = string.Join('\n', directiveLines) + "\n";
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)))
            .ToLowerInvariant()[..16];

        var dir = Path.Combine(root ?? DefaultDirectivesRoot(), hash);
        var path = Path.Combine(dir, "snippet.cs");
        if (File.Exists(path) && File.ReadAllText(path) == content)
            return path;

        Directory.CreateDirectory(dir);
        var temp = Path.Combine(dir, $".snippet.cs.tmp-{Guid.NewGuid():N}");
        File.WriteAllText(temp, content);
        File.Move(temp, path, overwrite: true);
        return path;
    }

    internal static List<string> ExtractDirectiveLines(string source)
    {
        return source.Split('\n')
            .Select(l => l.TrimEnd('\r').Trim())
            .Where(l => l.StartsWith("#:", StringComparison.Ordinal))
            .ToList();
    }

    private static string DefaultDirectivesRoot()
    {
        var env = Environment.GetEnvironmentVariable("GLOSHARP_CACHE_DIR");
        return !string.IsNullOrEmpty(env)
            ? Path.Combine(env, "file-based-apps")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glosharp", "file-based-apps");
    }

    internal static FileBasedAppResolveResult ParsePropertyOutput(string output, string sourceFilePath)
    {
        // MSBuild prints warnings/errors before the JSON document; parse from its first line.
        var lines = output.Split('\n');
        var start = Array.FindIndex(lines, l => l.TrimStart().StartsWith('{'));
        var json = start >= 0 ? string.Join('\n', lines[start..]) : output;

        try
        {
            using var doc = JsonDocument.Parse(json);
            var properties = doc.RootElement.GetProperty("Properties");

            var assetsFile = properties.GetProperty("ProjectAssetsFile").GetString();
            var tfm = properties.GetProperty("TargetFramework").GetString();
            if (string.IsNullOrEmpty(assetsFile) || string.IsNullOrEmpty(tfm))
                throw new InvalidOperationException(
                    $"The SDK did not report ProjectAssetsFile/TargetFramework for '{sourceFilePath}'. Output: {output.Trim()}");

            return new FileBasedAppResolveResult
            {
                AssetsFilePath = assetsFile,
                TargetFramework = tfm,
            };
        }
        catch (Exception ex) when (ex is JsonException or KeyNotFoundException)
        {
            throw new InvalidOperationException(
                $"Failed to parse the SDK's property output for '{sourceFilePath}'. Output: {output.Trim()}", ex);
        }
    }
}
