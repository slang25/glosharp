using System.Text.Json;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

public class ProjectAssetsResult
{
    public required List<MetadataReference> References { get; init; }
    public required List<PackageReference> Packages { get; init; }
    public required string TargetFramework { get; init; }

    /// <summary>
    /// Shared frameworks the project references besides Microsoft.NETCore.App, e.g.
    /// <c>Microsoft.AspNetCore.App</c> for Web SDK projects. Pass these to
    /// <see cref="FrameworkResolver.GetFrameworkReferences"/> as additional frameworks.
    /// </summary>
    public List<string> FrameworkReferences { get; init; } = [];

    /// <summary>
    /// Built outputs referenced from the project itself and its ProjectReferences
    /// (already included in <see cref="References"/>), for diagnostics/cache keys.
    /// </summary>
    public List<string> ProjectOutputPaths { get; init; } = [];

    /// <summary>Non-fatal problems (unbuilt project references, missing package files, …).</summary>
    public List<string> Warnings { get; init; } = [];
}

public static class ProjectAssetsResolver
{
    /// <summary>
    /// Validates a <c>--project</c> path and returns the project file it denotes: the path
    /// itself for a *.csproj file, or the single *.csproj in a directory. Throws a clear
    /// error for solutions, other files, missing paths and ambiguous directories.
    /// </summary>
    public static string FindProjectFile(string projectPath)
    {
        var full = Path.GetFullPath(projectPath);
        if (File.Exists(full))
        {
            var ext = Path.GetExtension(full);
            if (ext.Equals(".csproj", StringComparison.OrdinalIgnoreCase))
                return full;
            if (ext.Equals(".sln", StringComparison.OrdinalIgnoreCase) || ext.Equals(".slnx", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException(
                    $"'{projectPath}' is a solution. --project needs a single C# project: pass the .csproj " +
                    "whose packages and project references the snippets should compile against.");
            throw new ArgumentException(
                $"'{projectPath}' is not a C# project file. --project accepts a .csproj file or a directory containing one.");
        }

        if (Directory.Exists(full))
        {
            var projects = Directory.GetFiles(full, "*.csproj");
            if (projects.Length == 1)
                return projects[0];
            if (projects.Length > 1)
                throw new ArgumentException(
                    $"Directory '{projectPath}' contains more than one project ({string.Join(", ", projects.Select(Path.GetFileName))}). " +
                    "Pass the .csproj to use with --project.");
            throw new FileNotFoundException($"No .csproj found in directory '{projectPath}'.", full);
        }

        throw new FileNotFoundException($"Project path not found: {projectPath}", full);
    }

    /// <summary>
    /// Finds the project's project.assets.json: <c>obj/project.assets.json</c> next to the
    /// project, the artifacts layout (<c>artifacts/obj/&lt;project&gt;/</c>), and finally the
    /// <c>ProjectAssetsFile</c> MSBuild property (custom <c>BaseIntermediateOutputPath</c>).
    /// Throws <see cref="FileNotFoundException"/> when the project hasn't been restored.
    /// </summary>
    public static string FindAssetsFile(string projectPath)
    {
        string projectDir;
        string? projectFile = null;

        if (File.Exists(projectPath) && projectPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
        {
            projectFile = Path.GetFullPath(projectPath);
            projectDir = Path.GetDirectoryName(projectFile)!;
        }
        else if (Directory.Exists(projectPath))
        {
            projectDir = projectPath;
            var projects = Directory.GetFiles(projectDir, "*.csproj");
            if (projects.Length == 1)
                projectFile = projects[0];
        }
        else
        {
            throw new FileNotFoundException($"Project path not found: {projectPath}");
        }

        var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
        if (File.Exists(assetsPath))
            return assetsPath;

        if (projectFile != null)
        {
            var projectName = Path.GetFileNameWithoutExtension(projectFile);
            foreach (var artifacts in FindArtifactsDirectories(projectDir))
            {
                var candidate = Path.Combine(artifacts, "obj", projectName, "project.assets.json");
                if (File.Exists(candidate))
                    return candidate;
            }

            var queried = QueryAssetsFileProperty(projectFile);
            if (queried != null && File.Exists(queried))
                return queried;
            if (queried != null)
                assetsPath = queried;
        }

        throw new FileNotFoundException(
            $"project.assets.json not found at {assetsPath}. Run 'dotnet restore' first.", assetsPath);
    }

    /// <summary>
    /// Why <paramref name="projectFile"/> should be (re)restored before use, or null when its
    /// project.assets.json is present, newer than the project file, and free of restore errors.
    /// </summary>
    public static string? GetRestoreReason(string projectFile)
    {
        string assets;
        try
        {
            assets = FindAssetsFile(projectFile);
        }
        catch (FileNotFoundException)
        {
            return "project.assets.json is missing";
        }

        if (File.GetLastWriteTimeUtc(projectFile) > File.GetLastWriteTimeUtc(assets))
            return "the project file changed since the last restore";

        try
        {
            using var doc = JsonDocument.Parse(File.ReadAllText(assets));
            var errors = new List<string>();
            AddRestoreErrors(doc.RootElement, errors);
            if (errors.Count > 0)
                return "the last restore failed";
        }
        catch (JsonException)
        {
            return "project.assets.json is unreadable";
        }

        return null;
    }

    private static string? QueryAssetsFileProperty(string projectFile)
    {
        try
        {
            var result = ProcessRunner.Run(
                FrameworkResolver.GetDotnetExecutable(),
                ["msbuild", projectFile, "-getProperty:ProjectAssetsFile", "-nologo"],
                Path.GetDirectoryName(projectFile),
                TimeSpan.FromMinutes(2));
            if (!result.Succeeded)
                return null;
            var value = result.StandardOutput.Trim();
            return value.Length > 0 && !value.StartsWith('{') ? value : null;
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    public static ProjectAssetsResult Resolve(string assetsFilePath, string? targetFramework = null)
        => Resolve(assetsFilePath, targetFramework, includeProjectOutputs: true);

    /// <param name="includeProjectOutputs">
    /// Reference the built output of the project itself and of its ProjectReferences. False
    /// for file-based apps, whose "project" is the snippet being compiled.
    /// </param>
    public static ProjectAssetsResult Resolve(string assetsFilePath, string? targetFramework, bool includeProjectOutputs)
    {
        var json = File.ReadAllText(assetsFilePath);
        return ResolveFromJson(json, targetFramework, includeProjectOutputs);
    }

    internal static ProjectAssetsResult ResolveFromJson(string json, string? targetFramework = null, bool includeProjectOutputs = true)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;

        var packageFolders = GetPackageFolders(root);
        var warnings = new List<string>();
        AddRestoreErrors(root, warnings);

        var targets = root.GetProperty("targets");
        var (tfmKey, tfmShort) = SelectTargetFramework(targets, targetFramework, warnings);
        var target = targets.GetProperty(tfmKey);

        root.TryGetProperty("libraries", out var libraries);
        var projectFile = GetProjectFile(root);
        var projectDir = projectFile != null ? Path.GetDirectoryName(projectFile) : null;

        var references = new List<MetadataReference>();
        var packages = new List<PackageReference>();
        var projectOutputs = new List<string>();
        var frameworkReferences = new List<string>();
        AddFrameworkReferences(frameworkReferences, GetProjectFrameworkReferences(root, tfmKey, tfmShort));

        foreach (var entry in target.EnumerateObject())
        {
            var packageIdVersion = entry.Name; // e.g. "Newtonsoft.Json/13.0.3"
            var slashIndex = packageIdVersion.IndexOf('/');
            if (slashIndex < 0) continue;

            var packageId = packageIdVersion[..slashIndex];
            var packageVersion = packageIdVersion[(slashIndex + 1)..];

            if (entry.Value.TryGetProperty("frameworkReferences", out var entryFrameworks) &&
                entryFrameworks.ValueKind == JsonValueKind.Array)
            {
                AddFrameworkReferences(frameworkReferences,
                    entryFrameworks.EnumerateArray().Select(e => e.GetString()).OfType<string>());
            }

            var type = entry.Value.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : "package";
            if (string.Equals(type, "project", StringComparison.OrdinalIgnoreCase))
            {
                if (!includeProjectOutputs)
                    continue;
                ResolveProjectReference(entry, packageIdVersion, packageId, libraries, projectDir,
                    references, projectOutputs, warnings);
                continue;
            }

            var relativePaths = GetAssemblyRelativePaths(entry.Value);
            if (relativePaths.Count == 0) continue;

            var packageRoot = FindPackageRoot(packageFolders, libraries, packageIdVersion, packageId, packageVersion);
            var missing = 0;
            foreach (var relativePath in relativePaths)
            {
                var path = packageRoot != null
                    ? Path.Combine(packageRoot, relativePath.Replace('/', Path.DirectorySeparatorChar))
                    : null;
                if (path != null && File.Exists(path))
                    references.Add(CreateReference(path));
                else
                    missing++;
            }

            if (missing > 0 && packageFolders.Count > 0 && packageFolders.Any(Directory.Exists))
            {
                warnings.Add(
                    $"Package '{packageId} {packageVersion}' is in project.assets.json but its assemblies were not found " +
                    $"in the package folders ({string.Join(", ", packageFolders)}). Run 'dotnet restore'.");
            }

            packages.Add(new PackageReference
            {
                Name = packageId,
                Version = packageVersion,
            });
        }

        if (includeProjectOutputs && projectFile != null)
        {
            var own = ProjectOutputLocator.Find(projectFile, tfmShort);
            if (own != null)
            {
                references.Add(CreateReference(own));
                projectOutputs.Add(own);
            }
            else if (File.Exists(projectFile))
            {
                warnings.Add(
                    $"Project '{Path.GetFileName(projectFile)}' has no build output for {tfmShort}; its own types are " +
                    $"not available to snippets. Run 'dotnet build \"{projectFile}\"' first.");
            }
        }

        return new ProjectAssetsResult
        {
            References = references,
            Packages = packages,
            TargetFramework = tfmShort,
            FrameworkReferences = frameworkReferences,
            ProjectOutputPaths = projectOutputs,
            Warnings = warnings,
        };
    }

    private static MetadataReference CreateReference(string path) => MetadataReferenceCache.Get(path);

    private static void ResolveProjectReference(
        JsonProperty entry,
        string libraryKey,
        string projectName,
        JsonElement libraries,
        string? projectDir,
        List<MetadataReference> references,
        List<string> projectOutputs,
        List<string> warnings)
    {
        string? relativeProjectPath = null;
        if (libraries.ValueKind == JsonValueKind.Object &&
            libraries.TryGetProperty(libraryKey, out var library))
        {
            if (library.TryGetProperty("msbuildProject", out var msbuildProject))
                relativeProjectPath = msbuildProject.GetString();
            else if (library.TryGetProperty("path", out var libPath))
                relativeProjectPath = libPath.GetString();
        }

        if (relativeProjectPath == null || projectDir == null)
        {
            warnings.Add($"Project reference '{projectName}' could not be located from project.assets.json; its types are not available.");
            return;
        }

        var referencedProject = Path.GetFullPath(Path.Combine(projectDir, relativeProjectPath.Replace('/', Path.DirectorySeparatorChar)));
        var referencedTfm = entry.Value.TryGetProperty("framework", out var fw) && fw.GetString() is { } fwName
            ? ParseTfmFromKey(fwName)
            : null;

        var output = referencedTfm != null ? ProjectOutputLocator.Find(referencedProject, referencedTfm) : null;
        if (output != null)
        {
            references.Add(CreateReference(output));
            projectOutputs.Add(output);
        }
        else
        {
            warnings.Add(
                $"Project reference '{projectName}' ({referencedProject}) has no build output" +
                (referencedTfm != null ? $" for {referencedTfm}" : "") +
                $"; its types are not available to snippets. Run 'dotnet build' first.");
        }
    }

    /// <summary>
    /// A failed restore still writes project.assets.json, with the NuGet errors in its
    /// <c>logs</c> section; surface them instead of silently compiling without the packages.
    /// </summary>
    private static void AddRestoreErrors(JsonElement root, List<string> warnings)
    {
        if (!root.TryGetProperty("logs", out var logs) || logs.ValueKind != JsonValueKind.Array)
            return;

        foreach (var log in logs.EnumerateArray())
        {
            var level = log.TryGetProperty("level", out var l) ? l.GetString() : null;
            if (!string.Equals(level, "Error", StringComparison.OrdinalIgnoreCase))
                continue;
            var code = log.TryGetProperty("code", out var c) ? c.GetString() : null;
            var message = log.TryGetProperty("message", out var m) ? m.GetString() : null;
            warnings.Add($"The last restore failed: {code}: {message} Run 'dotnet restore' to fix it.");
        }
    }

    private static void AddFrameworkReferences(List<string> list, IEnumerable<string> names)
    {
        foreach (var name in names)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (name.Equals("Microsoft.NETCore.App", StringComparison.OrdinalIgnoreCase)) continue;
            if (!list.Contains(name, StringComparer.OrdinalIgnoreCase))
                list.Add(name);
        }
    }

    private static IEnumerable<string> GetProjectFrameworkReferences(JsonElement root, string tfmKey, string tfmShort)
    {
        if (!root.TryGetProperty("project", out var project) ||
            !project.TryGetProperty("frameworks", out var frameworks) ||
            frameworks.ValueKind != JsonValueKind.Object)
            yield break;

        foreach (var framework in frameworks.EnumerateObject())
        {
            var alias = framework.Value.TryGetProperty("targetAlias", out var ta) ? ta.GetString() : null;
            var matches = string.Equals(framework.Name, tfmKey, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(framework.Name, tfmShort, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(alias, tfmShort, StringComparison.OrdinalIgnoreCase) ||
                          string.Equals(ParseTfmFromKey(framework.Name), tfmShort, StringComparison.OrdinalIgnoreCase);
            if (!matches) continue;

            if (framework.Value.TryGetProperty("frameworkReferences", out var refs) &&
                refs.ValueKind == JsonValueKind.Object)
            {
                foreach (var r in refs.EnumerateObject())
                    yield return r.Name;
            }
        }
    }

    private static string? GetProjectFile(JsonElement root)
    {
        if (root.TryGetProperty("project", out var project) &&
            project.TryGetProperty("restore", out var restore) &&
            restore.TryGetProperty("projectPath", out var projectPath) &&
            projectPath.GetString() is { Length: > 0 } path)
            return path;
        return null;
    }

    private static List<string> GetPackageFolders(JsonElement root)
    {
        if (!root.TryGetProperty("packageFolders", out var folders))
            throw new InvalidOperationException("project.assets.json has no packageFolders section.");

        var list = folders.EnumerateObject().Select(f => f.Name).ToList();
        if (list.Count == 0)
            throw new InvalidOperationException("project.assets.json has empty packageFolders section.");
        return list;
    }

    /// <summary>
    /// The package's directory: <c>&lt;folder&gt;/&lt;libraries[key].path&gt;</c> in the first
    /// package folder (primary, then fallbacks) that contains it.
    /// </summary>
    private static string? FindPackageRoot(
        List<string> packageFolders, JsonElement libraries, string libraryKey, string packageId, string packageVersion)
    {
        string relative;
        if (libraries.ValueKind == JsonValueKind.Object &&
            libraries.TryGetProperty(libraryKey, out var library) &&
            library.TryGetProperty("path", out var pathElement) &&
            pathElement.GetString() is { Length: > 0 } libPath)
        {
            relative = libPath;
        }
        else
        {
            relative = $"{packageId.ToLowerInvariant()}/{packageVersion.ToLowerInvariant()}";
        }

        string? first = null;
        foreach (var folder in packageFolders)
        {
            var dir = Path.Combine(folder, relative.Replace('/', Path.DirectorySeparatorChar));
            first ??= dir;
            if (Directory.Exists(dir))
                return dir;
        }
        return first;
    }

    private static (string Key, string ShortTfm) SelectTargetFramework(JsonElement targets, string? requestedTfm, List<string> warnings)
    {
        var available = new List<(string Key, string ShortTfm)>();

        foreach (var target in targets.EnumerateObject())
        {
            // RID-specific targets ("net8.0/osx-arm64") duplicate the RID-less ones.
            if (target.Name.Contains('/')) continue;
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
                t.ShortTfm.Equals(requestedTfm, StringComparison.OrdinalIgnoreCase) ||
                t.Key.Equals(requestedTfm, StringComparison.OrdinalIgnoreCase));

            if (match.Key != null)
                return match;

            var availableList = string.Join(", ", available.Select(t => t.ShortTfm));
            throw new InvalidOperationException(
                $"Target framework '{requestedTfm}' not found in project. Available: {availableList}");
        }

        if (available.Count > 1)
        {
            warnings.Add(
                $"The project targets several frameworks ({string.Join(", ", available.Select(t => t.ShortTfm))}); " +
                $"using {available[0].ShortTfm}. Pass --framework to choose another.");
        }

        return available[0];
    }

    internal static string ParseTfmFromKey(string key)
    {
        // Keys look like "net8.0" or ".NETCoreApp,Version=v8.0"
        if (key.StartsWith("net", StringComparison.OrdinalIgnoreCase) && !key.Contains(','))
        {
            return key;
        }

        // Parse ".NETCoreApp,Version=v8.0" → "net8.0" (netcoreappX.Y below 5.0)
        if (key.StartsWith(".NETCoreApp,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            var version = key[".NETCoreApp,Version=v".Length..];
            return Version.TryParse(version, out var v) && v.Major < 5 ? $"netcoreapp{version}" : $"net{version}";
        }

        if (key.StartsWith(".NETStandard,Version=v", StringComparison.OrdinalIgnoreCase))
        {
            return $"netstandard{key[".NETStandard,Version=v".Length..]}";
        }

        return key;
    }

    private static List<string> GetAssemblyRelativePaths(JsonElement packageEntry)
    {
        var paths = new List<string>();

        // Try compile entries first, fall back to runtime entries
        if (!TryGetAssemblyPathsFromSection(packageEntry, "compile", paths))
            TryGetAssemblyPathsFromSection(packageEntry, "runtime", paths);
        return paths;
    }

    private static bool TryGetAssemblyPathsFromSection(JsonElement packageEntry, string sectionName, List<string> paths)
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

            paths.Add(relativePath);
            found = true;
        }

        return found;
    }

    internal static IEnumerable<string> FindArtifactsDirectories(string startDirectory)
    {
        var dir = new DirectoryInfo(Path.GetFullPath(startDirectory));
        while (dir != null)
        {
            var artifacts = Path.Combine(dir.FullName, "artifacts");
            if (Directory.Exists(Path.Combine(artifacts, "obj")) || Directory.Exists(Path.Combine(artifacts, "bin")))
                yield return artifacts;
            dir = dir.Parent;
        }
    }
}

/// <summary>
/// Finds a project's built assembly for a target framework by the SDK's output conventions:
/// <c>bin/&lt;Configuration&gt;/&lt;tfm&gt;[/&lt;rid&gt;]/</c>, and the artifacts layout
/// <c>artifacts/bin/&lt;Project&gt;/&lt;config&gt;[_&lt;tfm&gt;]/</c>. When several builds exist
/// (Debug and Release), the most recently written one wins.
/// </summary>
internal static class ProjectOutputLocator
{
    public static string? Find(string projectFile, string tfm)
    {
        var projectDir = Path.GetDirectoryName(projectFile);
        if (projectDir == null || !Directory.Exists(projectDir))
            return null;

        var projectName = Path.GetFileNameWithoutExtension(projectFile);
        var fileName = GetAssemblyName(projectFile) + ".dll";
        var candidates = new List<string>();

        var bin = Path.Combine(projectDir, "bin");
        if (Directory.Exists(bin))
        {
            foreach (var config in SafeDirectories(bin))
            {
                var tfmDir = FindTfmDirectory(config, tfm);
                if (tfmDir != null)
                {
                    candidates.Add(Path.Combine(tfmDir, fileName));
                    candidates.AddRange(SafeDirectories(tfmDir).Select(rid => Path.Combine(rid, fileName)));
                }
                candidates.Add(Path.Combine(config, fileName)); // AppendTargetFrameworkToOutputPath=false
            }
        }

        foreach (var artifacts in ProjectAssetsResolver.FindArtifactsDirectories(projectDir))
        {
            var projectBin = Path.Combine(artifacts, "bin", projectName);
            foreach (var pivot in SafeDirectories(projectBin))
            {
                var name = Path.GetFileName(pivot);
                var underscore = name.IndexOf('_');
                var pivotTfm = underscore >= 0 ? name[(underscore + 1)..] : null;
                if (pivotTfm == null || pivotTfm.Equals(tfm, StringComparison.OrdinalIgnoreCase) ||
                    pivotTfm.StartsWith(tfm + "_", StringComparison.OrdinalIgnoreCase))
                    candidates.Add(Path.Combine(pivot, fileName));
            }
        }

        return candidates
            .Where(File.Exists)
            .OrderByDescending(File.GetLastWriteTimeUtc)
            .FirstOrDefault();
    }

    private static string? FindTfmDirectory(string parent, string tfm)
    {
        return SafeDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), tfm, StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<string> SafeDirectories(string dir)
    {
        try
        {
            return Directory.Exists(dir) ? Directory.GetDirectories(dir) : [];
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    /// <summary>The literal &lt;AssemblyName&gt; from the project file, else the file name.</summary>
    internal static string GetAssemblyName(string projectFile)
    {
        var fallback = Path.GetFileNameWithoutExtension(projectFile);
        try
        {
            var doc = XDocument.Load(projectFile);
            var name = doc.Descendants()
                .Where(e => e.Name.LocalName == "AssemblyName")
                .Select(e => e.Value.Trim())
                .LastOrDefault(v => v.Length > 0 && !v.Contains("$("));
            return name ?? fallback;
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            return fallback;
        }
    }
}
