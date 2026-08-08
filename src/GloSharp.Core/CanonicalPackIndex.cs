using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;

namespace GloSharp.Core;

/// <summary>
/// Index of a canonical (NuGet-channel) targeting pack's ref assemblies, supporting the
/// three canonicalization match tiers: raw SHA-256, MVID, then file name + assembly version.
/// </summary>
internal sealed class CanonicalPackIndex
{
    internal sealed record Entry(string RelativePath, string Sha256, Guid Mvid, Version? AssemblyVersion);

    // A pack can carry the same assembly under several ref/<tfm>/ directories, so every
    // tier keeps all candidates (in sorted-path order) and disambiguates at match time.
    private readonly Dictionary<string, List<Entry>> _bySha = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, List<Entry>> _byMvid = new();
    private readonly Dictionary<(string Name, Version Version), List<Entry>> _byNameVersion = new();

    /// <summary>Content hash over the pack's ref DLLs (see <see cref="PackContentHasher"/>).</summary>
    public string ContentHash { get; private init; } = "";

    /// <summary>Relative paths of every ref DLL in the pack, sorted ordinal.</summary>
    public IReadOnlyList<string> RelativePaths { get; private init; } = Array.Empty<string>();

    public static CanonicalPackIndex Build(string packRoot)
    {
        var (contentHash, files) = PackContentHasher.HashRefDlls(packRoot);
        var index = new CanonicalPackIndex
        {
            ContentHash = contentHash,
            RelativePaths = files.Keys.OrderBy(p => p, StringComparer.Ordinal).ToList(),
        };

        foreach (var relative in files.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            var bytes = files[relative];
            Guid mvid;
            Version? asmVersion;
            try
            {
                (mvid, asmVersion) = ReadIdentity(bytes);
            }
            catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
            {
                continue;
            }

            var entry = new Entry(
                relative,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                mvid,
                asmVersion);

            Add(index._bySha, entry.Sha256, entry);
            Add(index._byMvid, mvid, entry);
            if (asmVersion != null)
                Add(index._byNameVersion,
                    (Path.GetFileName(relative).ToLowerInvariant(), asmVersion), entry);
        }
        return index;
    }

    /// <param name="originRelativePath">
    /// The reference's own <c>ref/&lt;tfm&gt;/&lt;file&gt;</c> path in the pack it came from,
    /// used to pick the right candidate when a pack repeats an assembly across tfm directories.
    /// </param>
    public Entry? Match(byte[] referenceBytes, Guid mvid, string fileName, string? originRelativePath = null)
    {
        var sha = Convert.ToHexString(SHA256.HashData(referenceBytes)).ToLowerInvariant();
        if (_bySha.TryGetValue(sha, out var candidates))
            return Choose(candidates, originRelativePath, requireUnambiguous: false);

        if (_byMvid.TryGetValue(mvid, out candidates))
            return Choose(candidates, originRelativePath, requireUnambiguous: false);

        Version? asmVersion;
        try
        {
            (_, asmVersion) = ReadIdentity(referenceBytes);
        }
        catch (Exception ex) when (ex is BadImageFormatException or InvalidOperationException)
        {
            return null;
        }
        if (asmVersion != null &&
            _byNameVersion.TryGetValue((fileName.ToLowerInvariant(), asmVersion), out candidates))
            return Choose(candidates, originRelativePath, requireUnambiguous: true);

        return null;
    }

    /// <summary>
    /// Picks one candidate: an exact origin-path hit first, then the sole candidate under the
    /// origin's tfm directory. SHA and MVID candidates are interchangeable (identical bytes,
    /// or the same build), so an unresolved tie falls back to the first path ordinal. A
    /// name+version tie is *not* interchangeable — the spec requires exactly one matching
    /// file — so it yields no match and the reference is embedded instead.
    /// </summary>
    private static Entry? Choose(List<Entry> candidates, string? originRelativePath, bool requireUnambiguous)
    {
        if (candidates.Count == 1)
            return candidates[0];

        if (originRelativePath != null)
        {
            var exact = candidates.FirstOrDefault(
                e => string.Equals(e.RelativePath, originRelativePath, StringComparison.Ordinal));
            if (exact != null)
                return exact;

            if (PackContentHasher.TfmOfDirectRefDll(originRelativePath) is { } tfm)
            {
                var sameTfm = candidates
                    .Where(e => PackContentHasher.TfmOfDirectRefDll(e.RelativePath) == tfm)
                    .ToList();
                if (sameTfm.Count == 1)
                    return sameTfm[0];
            }
        }

        return requireUnambiguous ? null : candidates[0];
    }

    private static void Add<TKey>(Dictionary<TKey, List<Entry>> index, TKey key, Entry entry)
        where TKey : notnull
    {
        if (!index.TryGetValue(key, out var list))
            index[key] = list = new List<Entry>();
        list.Add(entry);
    }

    private static (Guid Mvid, Version? AssemblyVersion) ReadIdentity(byte[] bytes)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        if (!peReader.HasMetadata)
            throw new BadImageFormatException("No CLI metadata");
        var mr = peReader.GetMetadataReader();
        var mvid = mr.GetGuid(mr.GetModuleDefinition().Mvid);
        Version? version = mr.IsAssembly ? mr.GetAssemblyDefinition().Version : null;
        return (mvid, version);
    }
}
