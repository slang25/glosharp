using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

public class ProjectAssetsResult
{
    public required List<MetadataReference> References { get; init; }
    public required List<PackageReference> Packages { get; init; }
    public required string TargetFramework { get; init; }
}

public static class ProjectAssetsResolver
{
    public static string FindAssetsFile(string projectPath)
    {
        string projectDir;

        if (File.Exists(projectPath) && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projectDir = Path.GetDirectoryName(projectPath)!;
        }
        else if (Directory.Exists(projectPath))
        {
            projectDir = projectPath;
        }
        else
        {
            throw new FileNotFoundException($"Project path not found: {projectPath}");
        }

        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        if (!File.Exists(assetsPath))
        {
            throw new FileNotFoundException(
                $"project.assets.json not found at {assetsPath}. Run 'dotnet restore' first.");
        }

        return assetsPath;
    }

    public static ProjectAssetsResult Resolve(string assetsFilePath, string? targetFramework = null)
    {
        var json = File.ReadAllText(assetsFilePath);
        return ResolveFromJson(json, targetFramework);
    }

    internal static ProjectAssetsResult ResolveFromJson(string json, string? targetFramework = null)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        // 1. Get package folders
        var packageFolder = GetPackageFolder(root);

        // 2. Select target framework
        var targets = root.GetProperty("targets");
        var (tfmKey, tfmShort) = SelectTargetFramework(targets, targetFramework);

        // 3. Get the target's packages
        var target = targets.GetProperty(tfmKey);

        // 4. Resolve assemblies and extract metadata
        var references = new List<MetadataReference>();
        var packages = new List<PackageReference>();

        foreach (var entry in target.EnumerateObject())
        {
            var packageIdVersion = entry.Name; // e.g. "Newtonsoft.Json/13.0.3"
            var slashIndex = packageIdVersion.IndexOf('/');
            if (slashIndex < 0) continue;

            var packageId = packageIdVersion[..slashIndex];
            var packageVersion = packageIdVersion[(slashIndex + 1)..];

            // Skip entries without compile or runtime assets
            var assemblyPaths = GetAssemblyPaths(entry.Value, packageFolder, packageId, packageVersion);
            if (assemblyPaths.Count == 0) continue;

            foreach (var path in assemblyPaths)
            {
                if (File.Exists(path))
                {
                    var xmlPath = Path.ChangeExtension(path, ".xml");
                    var docProvider = File.Exists(xmlPath)
                        ? XmlDocumentationProvider.CreateFromFile(xmlPath)
                        : null;
                    references.Add(MetadataReference.CreateFromFile(path, documentation: docProvider));
                }
            }

            packages.Add(new PackageReference
            {
                Name = packageId,
                Version = packageVersion,
            });
        }

        return new ProjectAssetsResult
        {
            References = references,
            Packages = packages,
            TargetFramework = tfmShort,
        };
    }

    private static string GetPackageFolder(JsonElement root)
    {
        if (!root.TryGetProperty("packageFolders", out var folders))
        {
            throw new InvalidOperationException("project.assets.json has no packageFolders section.");
        }

        foreach (var folder in folders.EnumerateObject())
        {
            return folder.Name;
        }

        throw new InvalidOperationException("project.assets.json has empty packageFolders section.");
    }

    private static (string Key, string ShortTfm) SelectTargetFramework(JsonElement targets, string? requestedTfm)
    {
        var available = new List<(string Key, string ShortTfm)>();

        foreach (var target in targets.EnumerateObject())
        {
            var shortTfm = ParseTfmFromKey(target.Name);
            available.Add((target.Name, shortTfm));
        }

        if (available.Count == 0)
        {
            throw new InvalidOperationException("project.assets.json has no target frameworks.");
        }

        if (requestedTfm != null)
        {
            var match = available.FirstOrDefault(t =>
                t.ShortTfm.Equals(requestedTfm, StringComparison.OrdinalIgnoreCase));

            if (match.Key != null)
                return match;

            var availableList = string.Join(", ", available.Select(t => t.ShortTfm));
            throw new InvalidOperationException(
                $"Target framework '{requestedTfm}' not found in project. Available: {availableList}");
        }

        return available[0];
    }

    private static string ParseTfmFromKey(string key)
    {
        // Keys look like "net8.0" or ".NETCoreApp,Version=v8.0"
        if (key.StartsWith("net", StringComparison.OrdinalIgnoreCase) && !key.Contains(','))
        {
            return key;
        }

        // Parse ".NETCoreApp,Version=v8.0" → "net8.0"
        if (key.StartsWith(".NETCoreApp,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            var version = key[".NETCoreApp,Version=v".Length..];
            return $"net{version}";
        }

        return key;
    }

    private static List<string> GetAssemblyPaths(
        JsonElement packageEntry,
        string packageFolder,
        string packageId,
        string packageVersion)
    {
        var paths = new List<string>();

        // Try compile entries first
        if (TryGetAssemblyPathsFromSection(packageEntry, "compile", packageFolder, packageId, packageVersion, paths))
        {
            return paths;
        }

        // Fall back to runtime entries
        TryGetAssemblyPathsFromSection(packageEntry, "runtime", packageFolder, packageId, packageVersion, paths);
        return paths;
    }

    private static bool TryGetAssemblyPathsFromSection(
        JsonElement packageEntry,
        string sectionName,
        string packageFolder,
        string packageId,
        string packageVersion,
        List<string> paths)
    {
        if (!packageEntry.TryGetProperty(sectionName, out var section))
            return false;

        var found = false;
        foreach (var asset in section.EnumerateObject())
        {
            var relativePath = asset.Name;

            // Skip placeholder entries like "lib/net6.0/_._"
            if (relativePath.EndsWith("_._", StringComparison.Ordinal))
                continue;

            // Skip non-DLL entries
            if (!relativePath.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                continue;

            var fullPath = Path.Combine(
                packageFolder,
                packageId.ToLowerInvariant(),
                packageVersion,
                relativePath);

            paths.Add(fullPath);
            found = true;
        }

        return found;
    }
}
