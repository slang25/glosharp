using System.Collections.Concurrent;
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
/// Locates targeting packs through an ordered chain of sources.
/// <para>
/// The installed SDK's <c>packs/</c> directory is only consulted when <em>reading</em> a
/// .glocontext (<see cref="CreateForReading"/>), never for compaction: its bytes are not
/// guaranteed to be canonical (source-built or re-signed SDKs differ from the NuGet-channel
/// packs), so a compactor must never record its hash. A reader can use it safely because
/// every located pack is verified against the manifest's content hash, and a candidate that
/// does not match is skipped in favour of the next source
/// (see <see cref="Locate(PackIdentity, Func{string, bool})"/>).
/// </para>
/// </summary>
public sealed class ReferencePackResolver
{
    private readonly IReadOnlyList<IPackSource> _sources;
    private readonly ConcurrentDictionary<PackIdentity, string?> _located = new();

    public ReferencePackResolver(IReadOnlyList<IPackSource> sources)
    {
        if (sources.Count == 0)
            throw new ArgumentException("At least one pack source is required", nameof(sources));
        _sources = sources;
    }

    /// <summary>
    /// The canonical chain used by the compactor: NuGet global packages folder, glosharp
    /// pack cache, then (optionally) a nuget.org download.
    /// </summary>
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

    /// <summary>
    /// The chain used when reading a .glocontext: <see cref="CreateDefault"/> plus the
    /// installed SDK's <c>packs/</c> directory, consulted before any download. A machine whose
    /// SDK ships the exact pack therefore needs no network, provided its bytes hash-verify.
    /// </summary>
    public static ReferencePackResolver CreateForReading(bool allowDownload = true)
    {
        var cacheRoot = GloSharpCacheRoot();
        var sources = new List<IPackSource>
        {
            new DirectoryPackSource(GlobalPackagesFolder(), "NuGet global packages folder"),
            new DirectoryPackSource(cacheRoot, "glosharp pack cache"),
        };
        if (FrameworkResolver.FindDotnetRoot() is { } dotnetRoot)
            sources.Add(new InstalledSdkPackSource(dotnetRoot));
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
        return _located.GetOrAdd(pack, p =>
        {
            foreach (var source in _sources)
            {
                var root = source.TryLocate(p);
                if (root != null)
                    return root;
            }
            return null;
        });
    }

    /// <summary>Returns the pack root directory or throws, enumerating every location consulted.</summary>
    public string Locate(PackIdentity pack)
    {
        return TryLocate(pack) ?? throw NotFound(pack);
    }

    /// <summary>
    /// Returns the first pack root, trying sources in order, that <paramref name="accept"/>
    /// approves (e.g. whose contents hash to the expected value). Throws
    /// <see cref="InvalidDataException"/> when the pack was found but no copy was accepted,
    /// and <see cref="InvalidOperationException"/> when no source could supply it.
    /// </summary>
    public string Locate(PackIdentity pack, Func<string, bool> accept)
    {
        var rejected = new List<string>();
        foreach (var source in _sources)
        {
            var root = source.TryLocate(pack);
            if (root == null)
                continue;
            if (accept(root))
                return root;
            rejected.Add(root);
        }

        if (rejected.Count > 0)
            throw new InvalidDataException(
                $"Content hash mismatch for targeting pack '{pack}': found at {string.Join(", ", rejected.Select(r => $"'{r}'"))}, " +
                "but the pack contents do not match what this .glocontext was created against.");

        throw NotFound(pack);
    }

    private InvalidOperationException NotFound(PackIdentity pack)
    {
        var searched = string.Join("\n", _sources.Select(s => $"  - {s.Describe(pack)}"));
        return new InvalidOperationException(
            $"Targeting pack '{pack}' could not be acquired. Locations searched:\n{searched}\n" +
            "Remedies: restore any project targeting this framework (populates the NuGet cache), " +
            "re-run with network access (the pack is then cached under GLOSHARP_CACHE_DIR, which CI can cache), " +
            "or ask the producer to re-create the .glocontext with --self-contained.");
    }

    /// <summary>
    /// Finds the XML documentation file for a pack-relative assembly path
    /// (<c>ref/net10.0/System.Runtime.dll</c> → <c>ref/net10.0/System.Runtime.xml</c>), first in
    /// <paramref name="preferredRoot"/>, then in every source that has the pack locally. Docs
    /// are cosmetic and not covered by the pack hash, so any local copy will do. Never downloads.
    /// </summary>
    public string? FindDocumentation(PackIdentity pack, string relativeDllPath, string? preferredRoot = null)
    {
        var relativeXml = Path.ChangeExtension(relativeDllPath, ".xml").Replace('/', Path.DirectorySeparatorChar);
        if (preferredRoot != null && File.Exists(Path.Combine(preferredRoot, relativeXml)))
            return Path.Combine(preferredRoot, relativeXml);

        foreach (var source in _sources)
        {
            if (source is NuGetDownloadSource)
                continue;
            var root = source.TryLocate(pack);
            if (root != null && File.Exists(Path.Combine(root, relativeXml)))
                return Path.Combine(root, relativeXml);
        }
        return null;
    }
}

/// <summary>
/// The installed SDK's targeting packs, <c>&lt;dotnet root&gt;/packs/&lt;Id&gt;/&lt;version&gt;</c>,
/// matched case-insensitively (the SDK uses display casing such as
/// <c>Microsoft.NETCore.App.Ref</c>; <see cref="PackIdentity"/> is lowercase).
/// </summary>
public sealed class InstalledSdkPackSource : IPackSource
{
    private readonly string _dotnetRoot;

    public InstalledSdkPackSource(string dotnetRoot)
    {
        _dotnetRoot = dotnetRoot;
    }

    public string? TryLocate(PackIdentity pack)
    {
        var packDir = FrameworkResolver.FindChildDirectoryIgnoreCase(Path.Combine(_dotnetRoot, "packs"), pack.Id);
        if (packDir == null)
            return null;
        var versionDir = FrameworkResolver.FindChildDirectoryIgnoreCase(packDir, pack.Version);
        return versionDir != null && Directory.Exists(Path.Combine(versionDir, "ref")) ? versionDir : null;
    }

    public string Describe(PackIdentity pack) =>
        $"installed .NET SDK packs: {Path.Combine(_dotnetRoot, "packs", pack.Id, pack.Version)}";
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
/// Downloads the pack's nupkg from nuget.org and extracts only <c>ref/**/*.dll</c> (plus
/// the XML documentation files beside them)
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
                    // ref DLLs (hash-verified on use) plus the XML docs beside them (for hovers).
                    if (!name.StartsWith("ref/", StringComparison.OrdinalIgnoreCase) ||
                        !(name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ||
                          name.EndsWith(".xml", StringComparison.OrdinalIgnoreCase)))
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
