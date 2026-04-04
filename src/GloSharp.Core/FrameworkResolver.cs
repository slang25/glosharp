using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

public static class FrameworkResolver
{
    public static string? FindFrameworkRefPath(string? targetFramework = null)
    {
        var dotnetRoot = FindDotnetRoot();
        if (dotnetRoot == null) return null;

        var packsDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
        if (!Directory.Exists(packsDir)) return null;

        // Get available versions, sorted descending
        var versions = Directory.GetDirectories(packsDir)
            .Select(d => new DirectoryInfo(d))
            .OrderByDescending(d => d.Name)
            .ToList();

        if (versions.Count == 0) return null;

        // If a target framework is specified, find matching version
        if (targetFramework != null)
        {
            var tfmVersion = targetFramework.Replace("net", "");
            var match = versions.FirstOrDefault(v => v.Name.StartsWith(tfmVersion + "."));
            if (match != null)
            {
                var refDir = Path.Combine(match.FullName, "ref", targetFramework);
                if (Directory.Exists(refDir)) return refDir;
            }
        }

        // Default: use latest version
        foreach (var version in versions)
        {
            var refDirs = Directory.GetDirectories(Path.Combine(version.FullName, "ref"));
            if (refDirs.Length > 0)
            {
                // Use the highest TFM available
                var latestTfm = refDirs
                    .Select(d => new DirectoryInfo(d))
                    .OrderByDescending(d => d.Name)
                    .First();
                return latestTfm.FullName;
            }
        }

        return null;
    }

    public static List<MetadataReference> GetFrameworkReferences(string? targetFramework = null)
    {
        var refPath = FindFrameworkRefPath(targetFramework);
        if (refPath == null)
        {
            throw new InvalidOperationException(
                $"Could not find .NET SDK framework reference assemblies. " +
                $"Ensure the .NET SDK is installed. " +
                (targetFramework != null ? $"Target framework '{targetFramework}' was not found." : ""));
        }

        return Directory.GetFiles(refPath, "*.dll")
            .Select(dll =>
            {
                var xmlPath = Path.ChangeExtension(dll, ".xml");
                var docProvider = File.Exists(xmlPath)
                    ? XmlDocumentationProvider.CreateFromFile(xmlPath)
                    : null;
                return (MetadataReference)MetadataReference.CreateFromFile(dll, documentation: docProvider);
            })
            .ToList();
    }

    private static string? FindDotnetRoot()
    {
        // Check DOTNET_ROOT env var
        var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");
        if (!string.IsNullOrEmpty(dotnetRoot) && Directory.Exists(dotnetRoot))
            return dotnetRoot;

        // Common install locations
        string[] paths =
        [
            "/usr/local/share/dotnet",                    // macOS
            "/usr/share/dotnet",                          // Linux
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "dotnet"), // Windows
        ];

        // Also check ~/.dotnet
        var homeDotnet = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");
        if (Directory.Exists(homeDotnet))
            return homeDotnet;

        foreach (var path in paths)
        {
            if (Directory.Exists(path))
                return path;
        }

        return null;
    }
}
