using System.Runtime.InteropServices;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <summary>
/// Locates the installed .NET SDK and resolves framework reference assemblies from its
/// targeting packs (<c>&lt;dotnet root&gt;/packs/&lt;Pack&gt;.Ref/&lt;version&gt;/ref/&lt;tfm&gt;</c>).
/// </summary>
public static class FrameworkResolver
{
    /// <summary>
    /// The target framework used when none is given (no <c>--framework</c>, no config, no
    /// project/complog). .NET 10 is the current LTS; .NET 8 leaves support on 2026-11-10.
    /// Every default in glosharp (processor, <c>glosharp init</c>) should use this constant.
    /// </summary>
    public const string DefaultTargetFramework = "net10.0";

    public const string NetCoreAppRefPack = "Microsoft.NETCore.App.Ref";

    /// <summary>
    /// Returns the ref directory for <paramref name="targetFramework"/> (or the newest installed
    /// framework when null), or null when it cannot be resolved. Prefer
    /// <see cref="ResolveFrameworkRefPath"/>, which explains why resolution failed.
    /// </summary>
    public static string? FindFrameworkRefPath(string? targetFramework = null)
    {
        try
        {
            return ResolveFrameworkRefPath(targetFramework);
        }
        catch (InvalidOperationException)
        {
            return null;
        }
    }

    /// <summary>
    /// Returns the ref directory of the Microsoft.NETCore.App targeting pack for
    /// <paramref name="targetFramework"/>: the highest installed pack version (semantic
    /// version order, stable above prerelease) whose major.minor matches the TFM. When
    /// <paramref name="targetFramework"/> is null, the newest installed framework is used.
    /// Throws <see cref="InvalidOperationException"/> with an actionable message for an
    /// invalid or unsupported TFM, a TFM that is not installed, or a missing SDK — it never
    /// silently substitutes a different framework.
    /// </summary>
    public static string ResolveFrameworkRefPath(string? targetFramework = null)
    {
        var dotnetRoot = FindDotnetRoot() ?? throw new InvalidOperationException(NoSdkMessage());
        return ResolvePackRefPath(dotnetRoot, NetCoreAppRefPack, targetFramework, NuGetPackageFolders());
    }

    public static List<MetadataReference> GetFrameworkReferences(string? targetFramework = null, IEnumerable<string>? additionalFrameworks = null)
    {
        var dotnetRoot = FindDotnetRoot() ?? throw new InvalidOperationException(NoSdkMessage());
        var packageFolders = NuGetPackageFolders();
        var refPath = ResolvePackRefPath(dotnetRoot, NetCoreAppRefPack, targetFramework, packageFolders);

        // Additional packs must match the base framework's version, which is what
        // targetFramework resolved to (it may have been null = newest installed).
        var effectiveTfm = targetFramework ?? Path.GetFileName(refPath);

        var refs = LoadReferencesFromPath(refPath);
        var seenPacks = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { NetCoreAppRefPack };

        if (additionalFrameworks != null)
        {
            foreach (var fw in additionalFrameworks)
            {
                var packName = ToRefPackName(fw);
                if (!seenPacks.Add(packName))
                    continue;
                var fwPath = ResolvePackRefPath(dotnetRoot, packName, effectiveTfm, packageFolders);
                refs.AddRange(LoadReferencesFromPath(fwPath));
            }
        }

        return refs;
    }

    /// <summary>
    /// Maps a framework reference name (<c>Microsoft.AspNetCore.App</c>, as used by
    /// <c>&lt;FrameworkReference&gt;</c> and project.assets.json) to its targeting pack
    /// name (<c>Microsoft.AspNetCore.App.Ref</c>). Pack names pass through unchanged.
    /// </summary>
    public static string ToRefPackName(string frameworkOrPackName)
    {
        var name = frameworkOrPackName.Trim();
        return name.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase) ? name : name + ".Ref";
    }

    /// <summary>
    /// Normalizes a TFM to its canonical lowercase short form (<c>NET10.0</c> → <c>net10.0</c>).
    /// Throws for TFMs glosharp cannot compile against.
    /// </summary>
    public static string NormalizeTargetFramework(string targetFramework)
    {
        var parsed = ParseTargetFramework(targetFramework);
        return parsed.Platform == null ? parsed.RefDirectoryName : $"{parsed.RefDirectoryName}-{parsed.Platform}";
    }

    private static List<MetadataReference> LoadReferencesFromPath(string refPath)
    {
        return Directory.GetFiles(refPath, "*.dll")
            .OrderBy(dll => dll, StringComparer.Ordinal)
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

    /// <summary>
    /// Targeting packs restored into the NuGet global packages folder count as installed too:
    /// restoring a project for a framework the SDK doesn't bundle downloads its pack there.
    /// </summary>
    private static IReadOnlyList<string> NuGetPackageFolders() => [ReferencePackResolver.GlobalPackagesFolder()];

    internal static string ResolvePackRefPath(
        string dotnetRoot, string packName, string? targetFramework, IReadOnlyList<string>? packageFolders = null)
    {
        var installed = new List<InstalledPack>();
        if (FindChildDirectoryIgnoreCase(Path.Combine(dotnetRoot, "packs"), packName) is { } sdkPackDir)
            installed.AddRange(EnumerateInstalled(sdkPackDir));
        foreach (var folder in packageFolders ?? [])
        {
            if (FindChildDirectoryIgnoreCase(folder, packName) is { } nugetPackDir)
                installed.AddRange(EnumerateInstalled(nugetPackDir));
        }

        if (targetFramework == null)
        {
            // Newest installed framework: highest pack version that has a ref directory.
            var newest = installed.OrderByDescending(i => i.PackVersion).FirstOrDefault();
            if (newest != null)
                return newest.RefDirectory;
            throw new InvalidOperationException(
                $"No '{packName}' targeting packs are installed under '{Path.Combine(dotnetRoot, "packs")}'. " +
                "Ensure the .NET SDK is installed.");
        }

        var requested = ParseTargetFramework(targetFramework);
        var match = installed
            .Where(i => i.FrameworkVersion == requested.Version)
            .OrderByDescending(i => i.PackVersion)
            .FirstOrDefault();
        if (match != null)
            return match.RefDirectory;

        var available = installed
            .Select(i => i.FrameworkVersion)
            .Distinct()
            .OrderBy(v => v)
            .Select(v => ParsedTargetFramework.RefDirectoryNameFor(v))
            .ToList();
        var packLabel = packName.Equals(NetCoreAppRefPack, StringComparison.OrdinalIgnoreCase)
            ? "Target framework"
            : $"Framework reference '{packName[..^".Ref".Length]}' for target framework";
        throw new InvalidOperationException(
            $"{packLabel} '{targetFramework}' is not installed: no '{packName}' targeting pack for " +
            $"{requested.RefDirectoryName} was found under '{Path.Combine(dotnetRoot, "packs")}'. " +
            (available.Count > 0
                ? $"Installed: {string.Join(", ", available)}. "
                : "No versions of this pack are installed. ") +
            $"Install the .NET {requested.Version.Major}.{requested.Version.Minor} SDK or choose an installed framework.");
    }

    private sealed record InstalledPack(PackVersion PackVersion, Version FrameworkVersion, string RefDirectory);

    private static List<InstalledPack> EnumerateInstalled(string packDir)
    {
        var result = new List<InstalledPack>();
        foreach (var versionDir in Directory.EnumerateDirectories(packDir))
        {
            if (!PackVersion.TryParse(Path.GetFileName(versionDir), out var packVersion))
                continue;
            var refRoot = Path.Combine(versionDir, "ref");
            if (!Directory.Exists(refRoot))
                continue;
            foreach (var refDir in Directory.EnumerateDirectories(refRoot))
            {
                if (TryParseTargetFramework(Path.GetFileName(refDir), out var parsed, out _) &&
                    parsed!.Platform == null)
                    result.Add(new InstalledPack(packVersion, parsed.Version, refDir));
            }
        }
        return result;
    }

    // ---------------------------------------------------------------------------------
    // Target framework parsing
    // ---------------------------------------------------------------------------------

    internal sealed record ParsedTargetFramework(Version Version, string? Platform)
    {
        public string RefDirectoryName => RefDirectoryNameFor(Version);

        public static string RefDirectoryNameFor(Version v) =>
            v.Major >= 5 ? $"net{v.Major}.{v.Minor}" : $"netcoreapp{v.Major}.{v.Minor}";
    }

    // .NET Framework short TFMs (net10/net11 are omitted: today they almost always mean .NET 10/11).
    private static readonly HashSet<string> NetFrameworkVersions = new(StringComparer.Ordinal)
    {
        "20", "35", "40", "403", "45", "451", "452", "46", "461", "462", "47", "471", "472", "48", "481",
    };

    internal static ParsedTargetFramework ParseTargetFramework(string targetFramework)
    {
        if (TryParseTargetFramework(targetFramework, out var parsed, out var error))
            return parsed!;
        throw new InvalidOperationException(error);
    }

    /// <summary>
    /// Parses .NET (Core) TFMs: <c>netX.Y</c> (X ≥ 5), <c>netcoreappX.Y</c>, with an optional
    /// platform suffix (<c>net8.0-windows</c>). Case-insensitive.
    /// </summary>
    internal static bool TryParseTargetFramework(string? targetFramework, out ParsedTargetFramework? parsed, out string error)
    {
        parsed = null;
        error = "";
        var tfm = targetFramework?.Trim() ?? "";
        if (tfm.Length == 0)
        {
            error = "Target framework is empty. Use a target framework moniker such as net8.0 or net10.0.";
            return false;
        }

        var lower = tfm.ToLowerInvariant();
        string? platform = null;
        var dash = lower.IndexOf('-');
        if (dash >= 0)
        {
            platform = lower[(dash + 1)..];
            lower = lower[..dash];
            if (platform.Length == 0)
            {
                error = $"'{tfm}' is not a valid target framework moniker. Use e.g. net8.0 or net10.0.";
                return false;
            }
        }

        if (lower.StartsWith("netstandard", StringComparison.Ordinal))
        {
            error = $"Target framework '{tfm}' is a .NET Standard target. Snippets are compiled against a " +
                    ".NET runtime targeting pack; use a framework such as net8.0 or net10.0 instead.";
            return false;
        }

        string versionText;
        bool coreApp;
        if (lower.StartsWith("netcoreapp", StringComparison.Ordinal))
        {
            versionText = lower["netcoreapp".Length..];
            coreApp = true;
        }
        else if (lower.StartsWith("net", StringComparison.Ordinal))
        {
            versionText = lower["net".Length..];
            coreApp = false;
        }
        else
        {
            error = $"'{tfm}' is not a valid target framework moniker. Use e.g. net8.0 or net10.0.";
            return false;
        }

        var parts = versionText.Split('.');
        if (parts.Length != 2 ||
            !int.TryParse(parts[0], System.Globalization.NumberStyles.None, null, out var major) ||
            !int.TryParse(parts[1], System.Globalization.NumberStyles.None, null, out var minor))
        {
            if (!coreApp && NetFrameworkVersions.Contains(versionText))
                error = $"Target framework '{tfm}' is a .NET Framework target, which is not supported. " +
                        "Use a .NET (Core) framework such as net8.0 or net10.0.";
            else if (!coreApp && versionText.Length is > 0 and <= 2 && versionText.All(char.IsAsciiDigit))
                error = $"'{tfm}' is not a valid target framework moniker. Did you mean net{versionText}.0?";
            else
                error = $"'{tfm}' is not a valid target framework moniker. Use e.g. net8.0 or net10.0.";
            return false;
        }

        if (coreApp ? major is < 1 or > 3 : major < 5)
        {
            error = $"'{tfm}' is not a valid target framework moniker. Use e.g. net8.0 or net10.0.";
            return false;
        }

        parsed = new ParsedTargetFramework(new Version(major, minor), platform);
        return true;
    }

    // ---------------------------------------------------------------------------------
    // dotnet root discovery
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Finds the .NET installation to take targeting packs from. Candidates, in order:
    /// <c>DOTNET_ROOT_&lt;ARCH&gt;</c>, <c>DOTNET_ROOT</c>, the muxer that launched us
    /// (<c>DOTNET_HOST_PATH</c>), the real path of <c>dotnet</c> on <c>PATH</c>, the
    /// well-known install locations, then <c>~/.dotnet</c>. A candidate is only accepted
    /// if it contains <c>packs/Microsoft.NETCore.App.Ref</c>, so a <c>~/.dotnet</c> that only
    /// holds global tools (or a runtime-only install) is skipped.
    /// </summary>
    public static string? FindDotnetRoot() => FindDotnetRoot(DotnetRootProbe.System);

    internal static string? FindDotnetRoot(DotnetRootProbe probe)
    {
        foreach (var candidate in EnumerateDotnetRootCandidates(probe))
        {
            if (IsUsableDotnetRoot(candidate))
                return Path.GetFullPath(candidate);
        }
        return null;
    }

    internal static IEnumerable<string> EnumerateDotnetRootCandidates(DotnetRootProbe probe)
    {
        var arch = RuntimeInformation.ProcessArchitecture.ToString().ToUpperInvariant();
        foreach (var name in new[] { $"DOTNET_ROOT_{arch}", "DOTNET_ROOT" })
        {
            var value = probe.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                yield return value;
        }

        var hostPath = probe.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && ResolveRealPath(hostPath) is { } hostReal)
            yield return Path.GetDirectoryName(hostReal)!;

        if (probe.FindOnPath("dotnet") is { } onPath && ResolveRealPath(onPath) is { } real)
            yield return Path.GetDirectoryName(real)!;

        foreach (var location in probe.KnownLocations)
            yield return location;

        if (!string.IsNullOrEmpty(probe.HomeDirectory))
            yield return Path.Combine(probe.HomeDirectory, ".dotnet");
    }

    private static bool IsUsableDotnetRoot(string candidate)
    {
        try
        {
            return Directory.Exists(candidate) &&
                   FindChildDirectoryIgnoreCase(Path.Combine(candidate, "packs"), NetCoreAppRefPack) != null;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return false;
        }
    }

    /// <summary>Follows symlinks to the final target; returns null when the file doesn't exist.</summary>
    internal static string? ResolveRealPath(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            var target = File.ResolveLinkTarget(path, returnFinalTarget: true);
            return target?.FullName ?? Path.GetFullPath(path);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            return null;
        }
    }

    /// <summary>
    /// The dotnet executable to spawn for restore/property queries: the muxer that launched
    /// this process when running as a .NET tool (<c>DOTNET_HOST_PATH</c>), else <c>dotnet</c>
    /// from <c>PATH</c>.
    /// </summary>
    public static string GetDotnetExecutable()
    {
        var hostPath = Environment.GetEnvironmentVariable("DOTNET_HOST_PATH");
        if (!string.IsNullOrWhiteSpace(hostPath) && File.Exists(hostPath))
            return hostPath;
        return "dotnet";
    }

    internal static string? FindChildDirectoryIgnoreCase(string parent, string name)
    {
        var exact = Path.Combine(parent, name);
        if (Directory.Exists(exact))
            return exact;
        if (!Directory.Exists(parent))
            return null;
        return Directory.EnumerateDirectories(parent)
            .FirstOrDefault(d => string.Equals(Path.GetFileName(d), name, StringComparison.OrdinalIgnoreCase));
    }

    private static string NoSdkMessage()
    {
        var searched = string.Join(", ", EnumerateDotnetRootCandidates(DotnetRootProbe.System).Distinct());
        return "Could not find a .NET SDK installation with framework reference assemblies " +
               "(packs/Microsoft.NETCore.App.Ref). Install the .NET SDK, or set DOTNET_ROOT to its " +
               $"install directory. Searched: {(searched.Length > 0 ? searched : "(no candidates)")}.";
    }
}

/// <summary>Environment inputs for dotnet root discovery (injectable for tests).</summary>
internal sealed class DotnetRootProbe
{
    public required Func<string, string?> GetEnvironmentVariable { get; init; }
    public required Func<string, string?> FindOnPath { get; init; }
    public required IReadOnlyList<string> KnownLocations { get; init; }
    public string? HomeDirectory { get; init; }

    public static DotnetRootProbe System => new()
    {
        GetEnvironmentVariable = Environment.GetEnvironmentVariable,
        FindOnPath = name => FindExecutableOnPath(name, Environment.GetEnvironmentVariable("PATH")),
        KnownLocations = DefaultKnownLocations(),
        HomeDirectory = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
    };

    internal static IReadOnlyList<string> DefaultKnownLocations()
    {
        if (OperatingSystem.IsWindows())
        {
            var locations = new List<string>();
            foreach (var folder in new[] { Environment.SpecialFolder.ProgramFiles, Environment.SpecialFolder.ProgramFilesX86 })
            {
                var path = Environment.GetFolderPath(folder);
                if (!string.IsNullOrEmpty(path))
                    locations.Add(Path.Combine(path, "dotnet"));
            }
            return locations;
        }

        if (OperatingSystem.IsMacOS())
        {
            return
            [
                "/usr/local/share/dotnet",          // official installer
                "/usr/local/share/dotnet/x64",      // x64 SDK on Apple silicon
                "/opt/homebrew/opt/dotnet/libexec", // Homebrew formula
                "/usr/local/opt/dotnet/libexec",
            ];
        }

        return
        [
            "/usr/lib/dotnet",          // Ubuntu/Fedora distro packages
            "/usr/share/dotnet",        // Microsoft packages
            "/usr/lib64/dotnet",
            "/usr/local/share/dotnet",
            "/opt/dotnet",
            "/snap/dotnet-sdk/current",
        ];
    }

    internal static string? FindExecutableOnPath(string name, string? pathVariable)
    {
        if (string.IsNullOrEmpty(pathVariable))
            return null;
        var fileNames = OperatingSystem.IsWindows() ? new[] { name + ".exe", name } : new[] { name };
        foreach (var dir in pathVariable.Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries))
        {
            foreach (var fileName in fileNames)
            {
                try
                {
                    var candidate = Path.Combine(dir.Trim('"'), fileName);
                    if (File.Exists(candidate))
                        return candidate;
                }
                catch (ArgumentException)
                {
                    // Malformed PATH entry.
                }
            }
        }
        return null;
    }
}

/// <summary>
/// A NuGet-style package version (<c>major.minor.patch[-prerelease][+metadata]</c>), ordered by
/// SemVer 2.0 rules: numeric components numerically, a prerelease below its release, and
/// prerelease identifiers compared numerically when both are numeric, else ordinally.
/// </summary>
internal sealed class PackVersion : IComparable<PackVersion>
{
    private PackVersion(int[] numbers, string[] prerelease, string original)
    {
        Numbers = numbers;
        Prerelease = prerelease;
        Original = original;
    }

    public int[] Numbers { get; }
    public string[] Prerelease { get; }
    public string Original { get; }
    public bool IsPrerelease => Prerelease.Length > 0;

    public static bool TryParse(string? text, out PackVersion version)
    {
        version = null!;
        if (string.IsNullOrWhiteSpace(text))
            return false;

        var s = text.Trim();
        var plus = s.IndexOf('+');
        if (plus >= 0) s = s[..plus];
        var dash = s.IndexOf('-');
        var core = dash >= 0 ? s[..dash] : s;
        var pre = dash >= 0 ? s[(dash + 1)..] : "";

        var parts = core.Split('.');
        if (parts.Length is < 2 or > 4)
            return false;
        var numbers = new int[4];
        for (var i = 0; i < parts.Length; i++)
        {
            if (!int.TryParse(parts[i], System.Globalization.NumberStyles.None, null, out numbers[i]))
                return false;
        }

        string[] prerelease = [];
        if (dash >= 0)
        {
            prerelease = pre.Split('.');
            if (prerelease.Any(p => p.Length == 0))
                return false;
        }

        version = new PackVersion(numbers, prerelease, text.Trim());
        return true;
    }

    public int CompareTo(PackVersion? other)
    {
        if (other is null) return 1;
        for (var i = 0; i < 4; i++)
        {
            var c = Numbers[i].CompareTo(other.Numbers[i]);
            if (c != 0) return c;
        }

        if (!IsPrerelease && !other.IsPrerelease) return 0;
        if (!IsPrerelease) return 1;
        if (!other.IsPrerelease) return -1;

        for (var i = 0; i < Math.Min(Prerelease.Length, other.Prerelease.Length); i++)
        {
            var a = Prerelease[i];
            var b = other.Prerelease[i];
            var aNum = int.TryParse(a, System.Globalization.NumberStyles.None, null, out var an);
            var bNum = int.TryParse(b, System.Globalization.NumberStyles.None, null, out var bn);
            int c;
            if (aNum && bNum) c = an.CompareTo(bn);
            else if (aNum) c = -1; // numeric identifiers sort below alphanumeric ones
            else if (bNum) c = 1;
            else c = string.Compare(a, b, StringComparison.OrdinalIgnoreCase);
            if (c != 0) return c;
        }
        return Prerelease.Length.CompareTo(other.Prerelease.Length);
    }

    public override string ToString() => Original;
}
