using System.Formats.Tar;
using System.Security.Cryptography;
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

    /// <param name="packResolver">
    /// Where targeting packs for pointer references come from. Null uses
    /// <see cref="ReferencePackResolver.CreateForReading"/> (NuGet cache, glosharp cache, the
    /// installed SDK's packs, then a nuget.org download); every pack is hash-verified.
    /// </param>
    public static GloContextResolver Open(string path, ReferencePackResolver? packResolver = null)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($".glocontext file not found: {path}", path);

        var allBytes = File.ReadAllBytes(path);
        if (allBytes.Length < GloContextFormat.HeaderSize)
            throw new InvalidDataException(
                $"File '{path}' is smaller than the minimum .glocontext header size.");

        var header = GloContextFormat.ReadHeader(allBytes.AsSpan(0, GloContextFormat.HeaderSize));

        var compressed = allBytes.AsSpan(GloContextFormat.HeaderSize);
        byte[] tarBytes;
        try
        {
            tarBytes = ZstdSharpCodec.Instance.Decompress(compressed);
        }
        catch (InvalidDataException ex)
        {
            throw new InvalidDataException($".glocontext '{path}' is truncated or corrupt: {ex.Message}", ex);
        }

        var (manifest, blobs) = ReadTar(tarBytes);

        if (manifest.Version is not (1 or 2))
            throw new InvalidDataException(
                $".glocontext manifest version {manifest.Version} is not supported by this reader (supported: 1, 2).");

        // The header version is the file's advertised contract, so it has to agree with the
        // manifest: otherwise a file labelled v1 — which v1 readers accept and which promises
        // to be self-contained — could still carry pointers and trigger pack acquisition.
        if (manifest.Version != header.Version)
            throw new InvalidDataException(
                $".glocontext header format version {header.Version} does not match manifest version {manifest.Version}.");

        var packs = manifest.Packs ?? new List<ManifestPack>();
        if (manifest.Version == 1 && packs.Count > 0)
            throw new InvalidDataException(
                "A v1 .glocontext must be self-contained but declares targeting packs; " +
                "pointer references require format version 2.");
        var pointerReader = new PointerReader(packs, packResolver);

        var compilations = new List<GloContextCompilation>(manifest.Compilations.Count);
        foreach (var mc in manifest.Compilations)
        {
            var references = new List<MetadataReference>(mc.References.Count);
            foreach (var r in mc.References)
            {
                r.Validate(packs.Count);

                if (manifest.Version == 1 && (r.IsPointer || r.IsPackAll))
                    throw new InvalidDataException(
                        $"A v1 .glocontext must embed every reference, but '{r.Display}' is a targeting-pack " +
                        "pointer; pointer references require format version 2.");

                if (r.IsPackAll)
                {
                    foreach (var (display, relativePath, fileBytes) in pointerReader.ExpandAll(r.PackAll!.Value, r.Tfm!))
                    {
                        references.Add(MetadataReference.CreateFromImage(
                            fileBytes,
                            properties: new MetadataReferenceProperties(kind: MetadataImageKind.Assembly),
                            documentation: pointerReader.Documentation(r.PackAll!.Value, relativePath),
                            filePath: display));
                    }
                    continue;
                }

                byte[] bytes;
                DocumentationProvider? documentation = null;
                if (r.IsPointer)
                {
                    bytes = pointerReader.Read(r);
                    documentation = pointerReader.Documentation(r.Pack!.Value, r.Path!);
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
                    documentation: documentation,
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

    public ComplogResolutionResult Resolve(string? projectName = null, string? targetFramework = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        if (_compilations.Count == 0)
            throw new InvalidOperationException(".glocontext contains no compilations.");

        var warnings = new List<string>();
        var selected = CompilationSelector.Select(
            _compilations,
            c => c.ProjectName,
            c => c.TargetFramework,
            projectName,
            targetFramework,
            ".glocontext",
            warnings);

        return new ComplogResolutionResult
        {
            References = selected.References,
            CompilationOptions = selected.CompilationOptions,
            ParseOptions = selected.ParseOptions,
            TargetFramework = selected.TargetFramework,
            ProjectName = selected.ProjectName,
            Warnings = warnings,
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

                // Blobs are content-addressed (the name is the SHA-256 of the bytes). Verify
                // it, so embedded references are as tamper-evident as pack pointers.
                var actual = Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
                if (!string.Equals(actual, hash, StringComparison.OrdinalIgnoreCase))
                    throw new InvalidDataException(
                        $".glocontext blob '{entry.Name}' is corrupt: its contents hash to {actual}.");

                blobs[hash] = bytes;
            }
        }

        if (manifest is null)
            throw new InvalidDataException(".glocontext is missing manifest.json");

        return (manifest, blobs);
    }

    /// <summary>
    /// Reads pointer references from targeting packs. Each pack is acquired and verified
    /// once — its content hash over ref/**/*.dll must match the manifest's recorded
    /// hash — after which pointer reads are served from the verified snapshot. A located
    /// copy that fails verification (e.g. a non-canonical installed-SDK pack) is skipped in
    /// favour of the next source.
    /// </summary>
    private sealed class PointerReader
    {
        private readonly List<ManifestPack> _packs;
        private readonly ReferencePackResolver _resolver;
        private readonly Dictionary<int, (string Root, Dictionary<string, byte[]> Files)> _verifiedByIndex = new();

        public PointerReader(List<ManifestPack> packs, ReferencePackResolver? resolver)
        {
            _packs = packs;
            _resolver = resolver ?? ReferencePackResolver.CreateForReading();
        }

        public byte[] Read(ManifestReference r)
        {
            var packIndex = r.Pack!.Value;
            var (_, files) = GetVerified(packIndex);

            if (!files.TryGetValue(r.Path!, out var bytes))
                throw new InvalidDataException(
                    $"Targeting pack '{_packs[packIndex].Id}/{_packs[packIndex].Version}' is missing file '{r.Path}' referenced by this .glocontext.");

            return bytes;
        }

        /// <summary>
        /// Expands a whole-pack reference: every direct-child DLL of ref/&lt;tfm&gt;/ in
        /// the verified pack, sorted by path — the mirror of the compactor's collapse check.
        /// </summary>
        public IEnumerable<(string Display, string RelativePath, byte[] Bytes)> ExpandAll(int packIndex, string tfm)
        {
            var (_, files) = GetVerified(packIndex);

            var paths = PackContentHasher.DirectRefDlls(files.Keys, tfm);
            if (paths.Count == 0)
                throw new InvalidDataException(
                    $"Targeting pack '{_packs[packIndex].Id}/{_packs[packIndex].Version}' has no ref assemblies under 'ref/{tfm}/'.");

            foreach (var path in paths)
                yield return (path.Substring(path.LastIndexOf('/') + 1), path, files[path]);
        }

        /// <summary>
        /// XML docs for a pack file (BCL hovers): the .xml beside it in the verified pack, or in
        /// any local copy of the pack. Docs aren't part of the pack hash; they're cosmetic.
        /// </summary>
        public DocumentationProvider? Documentation(int packIndex, string relativePath)
        {
            var (root, _) = GetVerified(packIndex);
            var identity = PackIdentity.TryCreate(_packs[packIndex].Id, _packs[packIndex].Version);
            if (identity == null)
                return null;
            var xml = _resolver.FindDocumentation(identity, relativePath, root);
            return xml != null ? XmlDocumentationProvider.CreateFromFile(xml) : null;
        }

        private (string Root, Dictionary<string, byte[]> Files) GetVerified(int packIndex)
        {
            if (!_verifiedByIndex.TryGetValue(packIndex, out var verified))
            {
                verified = AcquireAndVerify(packIndex);
                _verifiedByIndex[packIndex] = verified;
            }
            return verified;
        }

        private (string Root, Dictionary<string, byte[]> Files) AcquireAndVerify(int packIndex)
        {
            var pack = _packs[packIndex];
            if (pack.Sha256.Length != 64)
                throw new InvalidDataException(
                    $"Manifest pack '{pack.Id}/{pack.Version}' is missing a valid content hash.");

            // Manifest ids/versions are untrusted and end up in filesystem paths.
            var identity = PackIdentity.TryCreate(pack.Id, pack.Version)
                ?? throw new InvalidDataException(
                    $"Manifest pack entry {packIndex} has an invalid id or version " +
                    $"('{pack.Id}' / '{pack.Version}'): both must be non-empty single path segments.");

            Dictionary<string, byte[]>? verifiedFiles = null;
            var mismatches = new List<string>();
            string root;
            try
            {
                root = _resolver.Locate(identity, candidate =>
                {
                    var (contentHash, files) = PackContentHasher.HashRefDlls(candidate);
                    if (contentHash.Equals(pack.Sha256, StringComparison.OrdinalIgnoreCase))
                    {
                        verifiedFiles = files;
                        return true;
                    }
                    mismatches.Add($"'{candidate}' hashes to {contentHash}");
                    return false;
                });
            }
            catch (InvalidDataException)
            {
                throw new InvalidDataException(
                    $"Content hash mismatch for targeting pack '{pack.Id}/{pack.Version}': " +
                    $"manifest expects {pack.Sha256}, but {string.Join("; ", mismatches)}. " +
                    "The pack contents do not match what this .glocontext was created against.");
            }

            return (root, verifiedFiles!);
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
