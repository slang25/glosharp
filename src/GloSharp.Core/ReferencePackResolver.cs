using System.IO.Compression;

namespace GloSharp.Core;

/// <summary>
/// Identity of a NuGet targeting pack. Id and Version are normalized to lowercase and
/// validated as single path segments: both are combined into filesystem paths under the
/// packages folder and the download cache, and both originate in untrusted manifests, so
/// a value like <c>..</c> or <c>/etc</c> must never reach <see cref="Path.Combine"/>.
/// </summary>
public sealed record PackIdentity
{
    private static readonly char[] SeparatorChars = { '/', '\\', ':' };

    public PackIdentity(string id, string version)
    {
        Id = Normalize(id, nameof(id));
        Version = Normalize(version, nameof(version));
    }

    public string Id { get; }
    public string Version { get; }

    /// <summary>Returns null instead of throwing when either value is not a usable path segment.</summary>
    public static PackIdentity? TryCreate(string? id, string? version) =>
        IsValidSegment(id) && IsValidSegment(version) ? new PackIdentity(id!, version!) : null;

    public override string ToString() => $"{Id}/{Version}";

    private static string Normalize(string value, string paramName)
    {
        if (!IsValidSegment(value))
            throw new ArgumentException(
                $"Pack {paramName} '{value}' must be a non-empty single path segment.", paramName);
        return value.ToLowerInvariant();
    }

    private static bool IsValidSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return false;
        if (value is "." or "..")
            return false;
        if (value.IndexOfAny(SeparatorChars) >= 0)
            return false;
        return value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;
    }
}

/// <summary>
/// A place a targeting pack can be found. Returns the pack's root directory
/// (the directory containing <c>ref/&lt;tfm&gt;/*.dll</c>) or null.
/// </summary>
public interface IPackSource
{
    string? TryLocate(PackIdentity pack);

    /// <summary>Human-readable description of where this source looked, for error messages.</summary>
    string Describe(PackIdentity pack);
}

/// <summary>
/// Locates targeting packs through an ordered chain of sources. The installed SDK's
/// packs directory is deliberately not part of the default chain: its bytes are not
/// canonical (they differ from the NuGet-channel bytes in signing, and some assemblies
/// are different builds entirely), so pointer hashes could never verify against them.
/// </summary>
public sealed class ReferencePackResolver
{
    private readonly IReadOnlyList<IPackSource> _sources;
    private readonly Dictionary<PackIdentity, string?> _located = new();

    public ReferencePackResolver(IReadOnlyList<IPackSource> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("At least one pack source is required", nameof(sources));
        _sources = sources;
    }

    public static ReferencePackResolver CreateDefault(bool allowDownload = true)
    {
        var cacheRoot = GloSharpCacheRoot();
        var sources = new List<IPackSource>
        {
            new DirectoryPackSource(GlobalPackagesFolder(), "NuGet global packages folder"),
            new DirectoryPackSource(cacheRoot, "glosharp pack cache"),
        };
        if (allowDownload)
            sources.Add(new NuGetDownloadSource(cacheRoot));
        return new ReferencePackResolver(sources);
    }

    public static string GlobalPackagesFolder()
    {
        var env = Environment.GetEnvironmentVariable("NUGET_PACKAGES");
        if (!string.IsNullOrEmpty(env))
            return env;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".nuget", "packages");
    }

    public static string GloSharpCacheRoot()
    {
        var env = Environment.GetEnvironmentVariable("GLOSHARP_CACHE_DIR");
        if (!string.IsNullOrEmpty(env))
            return env;
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "glosharp", "packs");
    }

    /// <summary>Returns the pack root directory, or null when no source can supply it.</summary>
    public string? TryLocate(PackIdentity pack)
    {
        if (_located.TryGetValue(pack, out var cached))
            return cached;

        string? root = null;
        foreach (var source in _sources)
        {
            root = source.TryLocate(pack);
            if (root != null)
                break;
        }
        _located[pack] = root;
        return root;
    }

    /// <summary>Returns the pack root directory or throws, enumerating every location consulted.</summary>
    public string Locate(PackIdentity pack)
    {
        var root = TryLocate(pack);
        if (root != null)
            return root;

        var searched = string.Join("\n", _sources.Select(s => $"  - {s.Describe(pack)}"));
        throw new InvalidOperationException(
            $"Targeting pack '{pack}' could not be acquired. Locations searched:\n{searched}\n" +
            "Remedies: restore any project targeting this framework (populates the NuGet cache), " +
            "re-run with network access, or ask the producer to re-create the .glocontext with --self-contained.");
    }
}

/// <summary>
/// A directory laid out as <c>&lt;root&gt;/&lt;id&gt;/&lt;version&gt;/…</c> — the NuGet
/// global packages folder and the glosharp pack cache both use this shape.
/// </summary>
public sealed class DirectoryPackSource : IPackSource
{
    private readonly string _root;
    private readonly string _label;

    public DirectoryPackSource(string root, string label)
    {
        _root = root;
        _label = label;
    }

    public string? TryLocate(PackIdentity pack)
    {
        var dir = Path.Combine(_root, pack.Id, pack.Version);
        return Directory.Exists(Path.Combine(dir, "ref")) ? dir : null;
    }

    public string Describe(PackIdentity pack) =>
        $"{_label}: {Path.Combine(_root, pack.Id, pack.Version)}";
}

/// <summary>
/// Downloads the pack's nupkg from nuget.org and extracts only <c>ref/**/*.dll</c>
/// into the glosharp cache. Extraction goes to a temp directory first and is renamed
/// into place, so an interrupted download never leaves a partial cache entry.
/// </summary>
public sealed class NuGetDownloadSource : IPackSource
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromMinutes(5) };

    private readonly string _cacheRoot;
    private readonly string _baseUrl;

    public NuGetDownloadSource(string cacheRoot)
        : this(cacheRoot, "https://api.nuget.org/v3-flatcontainer")
    {
    }

    internal NuGetDownloadSource(string cacheRoot, string baseUrl)
    {
        _cacheRoot = cacheRoot;
        _baseUrl = baseUrl.TrimEnd('/');
    }

    public string? TryLocate(PackIdentity pack)
    {
        var target = Path.Combine(_cacheRoot, pack.Id, pack.Version);
        if (Directory.Exists(Path.Combine(target, "ref")))
            return target;

        var url = DownloadUrl(pack);
        var tempDir = Path.Combine(
            _cacheRoot, pack.Id, $".{pack.Version}.tmp-{Guid.NewGuid():N}");
        try
        {
            using var response = Http.Send(
                new HttpRequestMessage(HttpMethod.Get, url), HttpCompletionOption.ResponseHeadersRead);
            if (!response.IsSuccessStatusCode)
                throw new HttpRequestException(
                    $"nuget.org returned {(int)response.StatusCode} for {url}");

            Directory.CreateDirectory(tempDir);
            using (var zip = new ZipArchive(response.Content.ReadAsStream(), ZipArchiveMode.Read))
            {
                foreach (var entry in zip.Entries)
                {
                    var name = entry.FullName.Replace('\\', '/');
                    if (!name.StartsWith("ref/", StringComparison.OrdinalIgnoreCase) ||
                        !name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                        continue;
                    if (name.Contains(".."))
                        continue;

                    var dest = Path.Combine(tempDir, name);
                    Directory.CreateDirectory(Path.GetDirectoryName(dest)!);
                    entry.ExtractToFile(dest, overwrite: false);
                }
            }

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            try
            {
                Directory.Move(tempDir, target);
            }
            catch (IOException) when (Directory.Exists(Path.Combine(target, "ref")))
            {
                // A concurrent process won the rename; use its result.
                Directory.Delete(tempDir, recursive: true);
            }
            return target;
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidDataException)
        {
            try { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); } catch { }
            LastError = $"{DownloadUrl(pack)} ({ex.Message})";
            return null;
        }
    }

    internal string? LastError { get; private set; }

    public string Describe(PackIdentity pack) =>
        LastError != null && LastError.StartsWith(DownloadUrl(pack), StringComparison.Ordinal)
            ? $"nuget.org download: {LastError}"
            : $"nuget.org download: {DownloadUrl(pack)}";

    private string DownloadUrl(PackIdentity pack) =>
        $"{_baseUrl}/{pack.Id}/{pack.Version}/{pack.Id}.{pack.Version}.nupkg";
}
