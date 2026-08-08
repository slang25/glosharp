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

    public static CanonicalPackIndex Build(string packRoot)
    {
        var index = new CanonicalPackIndex();
        var refRoot = Path.Combine(packRoot, "ref");
        if (!Directory.Exists(refRoot))
            return index;

        var files = Directory.EnumerateFiles(refRoot, "*.dll", SearchOption.AllDirectories)
            .OrderBy(f => f, StringComparer.Ordinal);
        foreach (var file in files)
        {
            byte[] bytes;
            Guid mvid;
            Version? asmVersion;
            try
            {
                bytes = File.ReadAllBytes(file);
                (mvid, asmVersion) = ReadIdentity(bytes);
            }
            catch (Exception ex) when (ex is IOException or BadImageFormatException or InvalidOperationException)
            {
                continue;
            }

            var relative = Path.GetRelativePath(packRoot, file).Replace('\\', '/');
            var entry = new Entry(
                relative,
                Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant(),
                mvid,
                asmVersion);

            index._bySha.TryAdd(entry.Sha256, entry);
            index._byMvid.TryAdd(mvid, entry);
            if (asmVersion != null)
                index._byNameVersion.TryAdd(
                    (Path.GetFileName(file).ToLowerInvariant(), asmVersion), entry);
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
