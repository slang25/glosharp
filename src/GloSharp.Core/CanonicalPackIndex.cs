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

    private readonly Dictionary<string, Entry> _bySha = new(StringComparer.Ordinal);
    private readonly Dictionary<Guid, Entry> _byMvid = new();
    private readonly Dictionary<(string Name, Version Version), Entry> _byNameVersion = new();

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

            index._bySha.TryAdd(entry.Sha256, entry);
            index._byMvid.TryAdd(mvid, entry);
            if (asmVersion != null)
                index._byNameVersion.TryAdd(
                    (Path.GetFileName(relative).ToLowerInvariant(), asmVersion), entry);
        }
        return index;
    }

    public Entry? Match(byte[] referenceBytes, Guid mvid, string fileName)
    {
        var sha = Convert.ToHexString(SHA256.HashData(referenceBytes)).ToLowerInvariant();
        if (_bySha.TryGetValue(sha, out var entry))
            return entry;

        if (_byMvid.TryGetValue(mvid, out entry))
            return entry;

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
            _byNameVersion.TryGetValue((fileName.ToLowerInvariant(), asmVersion), out entry))
            return entry;

        return null;
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
