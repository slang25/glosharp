using System.Formats.Tar;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

public sealed class GloContextResolver : IDisposable
{
    private readonly List<GloContextCompilation> _compilations;
    private bool _disposed;

    private GloContextResolver(List<GloContextCompilation> compilations)
    {
        _compilations = compilations;
    }

    public static GloContextResolver Open(string path, ReferencePackResolver? packResolver = null)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($".glocontext file not found: {path}", path);

        var allBytes = File.ReadAllBytes(path);
        if (allBytes.Length < GloContextFormat.HeaderSize)
            throw new InvalidDataException(
                $"File '{path}' is smaller than the minimum .glocontext header size.");

        GloContextFormat.ReadHeader(allBytes.AsSpan(0, GloContextFormat.HeaderSize));

        var compressed = allBytes.AsSpan(GloContextFormat.HeaderSize);
        var tarBytes = ZstdSharpCodec.Instance.Decompress(compressed);

        var (manifest, blobs) = ReadTar(tarBytes);

        if (manifest.Version is not (1 or 2))
            throw new InvalidDataException(
                $".glocontext manifest version {manifest.Version} is not supported by this reader (supported: 1, 2).");

        var packs = manifest.Packs ?? new List<ManifestPack>();
        var pointerReader = new PointerReader(packs, packResolver);

        var compilations = new List<GloContextCompilation>(manifest.Compilations.Count);
        foreach (var mc in manifest.Compilations)
        {
            var references = new List<MetadataReference>(mc.References.Count);
            foreach (var r in mc.References)
            {
                r.Validate(packs.Count);

                byte[] bytes;
                if (r.IsPointer)
                {
                    bytes = pointerReader.Read(r);
                }
                else if (!blobs.TryGetValue(r.Blob!, out bytes!))
                {
                    throw new InvalidDataException(
                        $".glocontext references missing blob '{r.Blob}' for '{r.Display}'.");
                }

                var reference = MetadataReference.CreateFromImage(
                    bytes,
                    properties: new MetadataReferenceProperties(
                        kind: MetadataImageKind.Assembly,
                        aliases: r.Aliases.ToImmutableArrayShim(),
                        embedInteropTypes: r.EmbedInteropTypes),
                    filePath: r.Display);

                references.Add(reference);
            }

            compilations.Add(new GloContextCompilation
            {
                ProjectName = mc.ProjectName,
                TargetFramework = mc.TargetFramework,
                CompilationOptions = ManifestOptionsMapper.Restore(mc.CompilationOptions),
                ParseOptions = ManifestOptionsMapper.Restore(mc.ParseOptions),
                References = references,
            });
        }

        return new GloContextResolver(compilations);
    }

    public ComplogResolutionResult Resolve(string? projectName = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_compilations.Count == 0)
            throw new InvalidOperationException(".glocontext contains no compilations.");

        GloContextCompilation selected;
        if (projectName != null)
        {
            selected = _compilations.FirstOrDefault(c =>
                string.Equals(c.ProjectName, projectName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Project '{projectName}' not found in .glocontext. Available projects: {string.Join(", ", _compilations.Select(c => c.ProjectName))}");
        }
        else
        {
            selected = _compilations[0];
        }

        return new ComplogResolutionResult
        {
            References = selected.References,
            CompilationOptions = selected.CompilationOptions,
            ParseOptions = selected.ParseOptions,
            TargetFramework = selected.TargetFramework,
        };
    }

    public void Dispose()
    {
        _disposed = true;
    }

    private static (GloContextManifest manifest, Dictionary<string, byte[]> blobs) ReadTar(byte[] tarBytes)
    {
        GloContextManifest? manifest = null;
        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        using var ms = new MemoryStream(tarBytes, writable: false);
        using var tarReader = new TarReader(ms, leaveOpen: true);

        while (tarReader.GetNextEntry(copyData: false) is { } entry)
        {
            if (entry.EntryType != TarEntryType.RegularFile) continue;

            using var entryStream = new MemoryStream();
            entry.DataStream?.CopyTo(entryStream);
            var bytes = entryStream.ToArray();

            if (entry.Name == "manifest.json")
            {
                manifest = ManifestSerializer.Deserialize(bytes);
            }
            else if (entry.Name.StartsWith("refs/", StringComparison.Ordinal) &&
                     entry.Name.EndsWith(".dll", StringComparison.Ordinal))
            {
                var hash = entry.Name.Substring("refs/".Length,
                    entry.Name.Length - "refs/".Length - ".dll".Length);
                blobs[hash] = bytes;
            }
        }

        if (manifest is null)
            throw new InvalidDataException(".glocontext is missing manifest.json");

        return (manifest, blobs);
    }

    /// <summary>
    /// Reads and verifies pointer references from targeting packs. Pack contents are
    /// cached per pack; every pointed file's SHA-256 is verified against the manifest.
    /// </summary>
    private sealed class PointerReader
    {
        private readonly List<ManifestPack> _packs;
        private readonly ReferencePackResolver _resolver;
        private readonly Dictionary<int, string> _rootsByIndex = new();

        public PointerReader(List<ManifestPack> packs, ReferencePackResolver? resolver)
        {
            _packs = packs;
            _resolver = resolver ?? ReferencePackResolver.CreateDefault();
        }

        public byte[] Read(ManifestReference r)
        {
            var packIndex = r.Pack!.Value;
            if (!_rootsByIndex.TryGetValue(packIndex, out var root))
            {
                var pack = _packs[packIndex];
                root = _resolver.Locate(new PackIdentity(pack.Id, pack.Version));
                _rootsByIndex[packIndex] = root;
            }

            var filePath = System.IO.Path.Combine(root, r.Path!.Replace('/', System.IO.Path.DirectorySeparatorChar));
            if (!File.Exists(filePath))
                throw new InvalidDataException(
                    $"Targeting pack '{_packs[packIndex].Id}/{_packs[packIndex].Version}' is missing file '{r.Path}' referenced by this .glocontext.");

            var bytes = File.ReadAllBytes(filePath);
            var actual = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(bytes)).ToLowerInvariant();
            if (!actual.Equals(r.Sha256, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException(
                    $"Hash mismatch for '{r.Path}' in targeting pack '{_packs[packIndex].Id}/{_packs[packIndex].Version}': " +
                    $"manifest expects {r.Sha256}, actual file is {actual}. " +
                    "The pack contents do not match what this .glocontext was created against.");

            return bytes;
        }
    }

    private sealed class GloContextCompilation
    {
        public required string ProjectName { get; init; }
        public required string TargetFramework { get; init; }
        public required Microsoft.CodeAnalysis.CSharp.CSharpCompilationOptions CompilationOptions { get; init; }
        public required Microsoft.CodeAnalysis.CSharp.CSharpParseOptions ParseOptions { get; init; }
        public required List<MetadataReference> References { get; init; }
    }
}

internal static class AliasesExtensions
{
    public static System.Collections.Immutable.ImmutableArray<string> ToImmutableArrayShim(this List<string> list)
    {
        return list.Count == 0
            ? System.Collections.Immutable.ImmutableArray<string>.Empty
            : System.Collections.Immutable.ImmutableArray.CreateRange(list);
    }
}
