using System.Formats.Tar;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;
using System.Security.Cryptography;
using Basic.CompilerLog.Util;
using JetBrains.Refasmer;
using JetBrains.Refasmer.Filters;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

public sealed record ComplogCompactionOptions
{
    public bool RewriteReferences { get; init; } = true;
    public bool DropAnalyzers { get; init; } = true;
    public bool DropOriginalSources { get; init; } = true;
    public bool DropGeneratedSources { get; init; } = true;

    /// <summary>
    /// When true, every reference is embedded as a blob (format v1) and no
    /// targeting-pack pointers are emitted. Use for artifacts that must resolve
    /// offline without any pack acquisition.
    /// </summary>
    public bool SelfContained { get; init; }

    /// <summary>
    /// Pack acquisition used for canonicalization. Null uses the default chain
    /// (NuGet global packages folder, glosharp cache, nuget.org download).
    /// </summary>
    public ReferencePackResolver? PackResolver { get; init; }

    public int ZstdLevel { get; init; } = 19;
    public int ZstdWindowLog { get; init; } = 27;
}

public sealed record ComplogCompactionResult
{
    public required long InputSizeBytes { get; init; }
    public required long OutputSizeBytes { get; init; }
    public required int ReferencesBefore { get; init; }
    public required int ReferencesAfter { get; init; }
    public required int RefasmRewrittenCount { get; init; }

    /// <summary>Unique references canonicalized to targeting-pack pointers.</summary>
    public required int PointersCreated { get; init; }

    /// <summary>Distinct packs referenced by pointers, as "id/version".</summary>
    public required IReadOnlyList<string> PointerPacks { get; init; }

    /// <summary>Non-fatal issues, e.g. packs that could not be acquired (refs were embedded instead).</summary>
    public required IReadOnlyList<string> Warnings { get; init; }

    public required int AnalyzersDropped { get; init; }
    public required int OriginalSourcesDropped { get; init; }
    public required int GeneratedSourcesDropped { get; init; }
}

public static class ComplogCompactor
{
    private static readonly DateTimeOffset DeterministicTime =
        new(2000, 1, 1, 0, 0, 0, TimeSpan.Zero);

    public static ComplogCompactionResult Compact(
        string inputPath,
        string outputPath,
        ComplogCompactionOptions options)
    {
        if (!File.Exists(inputPath))
            throw new FileNotFoundException($"Complog file not found: {inputPath}", inputPath);

        var fullOutputPath = Path.GetFullPath(outputPath);
        var outputDir = Path.GetDirectoryName(fullOutputPath)!;
        if (!Directory.Exists(outputDir))
            throw new IOException($"Output directory does not exist: {outputDir}");

        var inputSize = new FileInfo(inputPath).Length;

        using var reader = CompilerCallReaderUtil.Create(inputPath, BasicAnalyzerKind.None);
        var calls = reader.ReadAllCompilerCalls(c => c.IsCSharp);
        if (calls.Count == 0)
            throw new InvalidOperationException("Complog contains no C# compilations");

        var packResolver = options.SelfContained
            ? null
            : options.PackResolver ?? ReferencePackResolver.CreateDefault();
        var packIndexes = new Dictionary<PackIdentity, CanonicalPackIndex?>();
        var warnings = new List<string>();

        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var mvidToEntry = new Dictionary<Guid, ResolvedReference>();
        var pointerTargets = new HashSet<string>(StringComparer.Ordinal);

        int referencesBefore = 0;
        int refasmRewrittenCount = 0;
        int analyzersDropped = 0;
        int originalSourcesDropped = 0;
        int generatedSourcesDropped = 0;

        var compilations = new List<(ManifestCompilation Compilation, List<(ResolvedReference Entry, ManifestReference Occurrence)> Refs)>();

        foreach (var call in calls)
        {
            var references = reader.ReadAllReferenceData(call);
            referencesBefore += references.Count;

            if (options.DropAnalyzers)
                analyzersDropped += reader.ReadAllAnalyzerData(call).Count;

            if (options.DropOriginalSources)
                originalSourcesDropped += reader.ReadAllSourceTextData(call).Count;

            var (compilationOptions, parseOptions) = ParseCompilerArguments(reader, call);

            var manifestCompilation = new ManifestCompilation
            {
                ProjectName = Path.GetFileNameWithoutExtension(call.ProjectFileName),
                TargetFramework = call.TargetFramework ?? "",
                Language = "csharp",
                CompilationOptions = ManifestOptionsMapper.Capture(compilationOptions),
                ParseOptions = ManifestOptionsMapper.Capture(parseOptions),
                References = new List<ManifestReference>(),
            };

            var refs = new List<(ResolvedReference, ManifestReference)>();
            foreach (var refData in references)
            {
                if (!mvidToEntry.TryGetValue(refData.Mvid, out var entry))
                {
                    entry = ResolveReference(
                        reader, refData, options, packResolver, packIndexes, warnings,
                        blobs, pointerTargets, ref refasmRewrittenCount);
                    mvidToEntry[refData.Mvid] = entry;
                }

                var occurrence = new ManifestReference
                {
                    Display = refData.FileName,
                    Aliases = refData.Aliases.ToList(),
                    EmbedInteropTypes = refData.EmbedInteropTypes,
                };
                refs.Add((entry, occurrence));
            }

            compilations.Add((manifestCompilation, refs));
        }

        // Assign pack indexes deterministically (sorted by id, then version) and
        // materialize the per-occurrence reference entries.
        var usedPacks = compilations
            .SelectMany(c => c.Refs)
            .Where(r => r.Entry.Pack is not null)
            .Select(r => r.Entry.Pack!)
            .Distinct()
            .OrderBy(p => p.Id, StringComparer.Ordinal)
            .ThenBy(p => p.Version, StringComparer.Ordinal)
            .ToList();
        var packToIndex = usedPacks
            .Select((p, i) => (p, i))
            .ToDictionary(t => t.p, t => t.i);

        var manifest = new GloContextManifest
        {
            Version = usedPacks.Count > 0 ? 2 : 1,
            Packs = usedPacks.Count > 0
                ? usedPacks.Select(p => new ManifestPack
                {
                    Id = p.Id,
                    Version = p.Version,
                    Sha256 = packIndexes[p]!.ContentHash,
                }).ToList()
                : null,
        };

        foreach (var (manifestCompilation, refs) in compilations)
        {
            foreach (var (entry, occurrence) in refs)
            {
                manifestCompilation.References.Add(new ManifestReference
                {
                    Blob = entry.BlobHash,
                    Pack = entry.Pack is not null ? packToIndex[entry.Pack] : null,
                    Path = entry.PackRelativePath,
                    Display = occurrence.Display,
                    Aliases = occurrence.Aliases,
                    EmbedInteropTypes = occurrence.EmbedInteropTypes,
                });
            }
            manifest.Compilations.Add(manifestCompilation);
        }

        var manifestJson = ManifestSerializer.Serialize(manifest);
        var tarBytes = BuildTar(manifestJson, blobs);
        var compressed = ZstdSharpCodec.Instance.Compress(
            tarBytes, options.ZstdLevel, options.ZstdWindowLog);

        var output = new byte[GloContextFormat.HeaderSize + compressed.Length];
        GloContextFormat.WriteHeader(
            output, usedPacks.Count > 0 ? GloContextFormat.Version2 : GloContextFormat.Version1);
        compressed.CopyTo(output, GloContextFormat.HeaderSize);

        WriteAtomic(fullOutputPath, output);

        return new ComplogCompactionResult
        {
            InputSizeBytes = inputSize,
            OutputSizeBytes = output.Length,
            ReferencesBefore = referencesBefore,
            ReferencesAfter = blobs.Count,
            RefasmRewrittenCount = refasmRewrittenCount,
            PointersCreated = pointerTargets.Count,
            PointerPacks = usedPacks.Select(p => p.ToString()).ToList(),
            Warnings = warnings,
            AnalyzersDropped = analyzersDropped,
            OriginalSourcesDropped = originalSourcesDropped,
            GeneratedSourcesDropped = generatedSourcesDropped,
        };
    }

    /// <summary>A deduped reference: either an embedded blob or a pack pointer.</summary>
    private sealed record ResolvedReference
    {
        public string? BlobHash { get; init; }
        public PackIdentity? Pack { get; init; }
        public string? PackRelativePath { get; init; }
    }

    private static ResolvedReference ResolveReference(
        ICompilerCallReader reader,
        ReferenceData refData,
        ComplogCompactionOptions options,
        ReferencePackResolver? packResolver,
        Dictionary<PackIdentity, CanonicalPackIndex?> packIndexes,
        List<string> warnings,
        Dictionary<string, byte[]> blobs,
        HashSet<string> pointerTargets,
        ref int refasmRewrittenCount)
    {
        var bytes = ReadReferenceBytes(reader, refData);

        if (packResolver != null &&
            TryParsePackOrigin(refData.FilePath) is { } origin)
        {
            if (!packIndexes.TryGetValue(origin.Pack, out var index))
            {
                var root = packResolver.TryLocate(origin.Pack);
                index = root != null ? CanonicalPackIndex.Build(root) : null;
                if (index == null)
                    warnings.Add(
                        $"Targeting pack '{origin.Pack}' could not be acquired; its references were embedded instead of pointered.");
                packIndexes[origin.Pack] = index;
            }

            if (index != null)
            {
                var match = index.Match(bytes, refData.Mvid, refData.FileName);
                if (match != null)
                {
                    pointerTargets.Add($"{origin.Pack}:{match.RelativePath}");
                    return new ResolvedReference
                    {
                        Pack = origin.Pack,
                        PackRelativePath = match.RelativePath,
                    };
                }

                warnings.Add(
                    $"Reference '{refData.FileName}' originates from pack '{origin.Pack}' but matches no canonical file; embedded instead.");
            }
        }

        if (options.RewriteReferences && !IsReferenceAssembly(bytes))
        {
            bytes = RefasmBytes(bytes, refData.FileName);
            refasmRewrittenCount++;
        }

        var hash = ComputeSha256(bytes);
        if (!blobs.ContainsKey(hash))
            blobs[hash] = bytes;
        return new ResolvedReference { BlobHash = hash };
    }

    /// <summary>
    /// Recognizes targeting-pack reference paths and extracts the pack identity.
    /// Handles both the installed SDK layout (…/packs/Microsoft.NETCore.App.Ref/10.0.9/ref/net10.0/X.dll)
    /// and the NuGet global-packages layout (…/microsoft.netcore.app.ref/10.0.9/ref/net10.0/X.dll).
    /// Gated on the pack id ending in ".app.ref" so arbitrary package paths never match.
    /// </summary>
    internal static (PackIdentity Pack, string RelativePath)? TryParsePackOrigin(string? filePath)
    {
        if (string.IsNullOrEmpty(filePath))
            return null;

        var segments = filePath.Replace('\\', '/').Split('/');
        // Expect: … <id> <version> "ref" <tfm> <file.dll>
        if (segments.Length < 5)
            return null;

        var file = segments[^1];
        var tfm = segments[^2];
        var refDir = segments[^3];
        var version = segments[^4];
        var id = segments[^5];

        if (!refDir.Equals("ref", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!file.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return null;
        if (!id.EndsWith(".app.ref", StringComparison.OrdinalIgnoreCase))
            return null;

        return (new PackIdentity(id, version), $"ref/{tfm}/{file}");
    }

    private static (CSharpCompilationOptions, CSharpParseOptions) ParseCompilerArguments(
        ICompilerCallReader reader, CompilerCall call)
    {
        var args = reader.ReadArguments(call).ToArray();
        var baseDir = call.ProjectDirectory;
        var parsed = Microsoft.CodeAnalysis.CSharp.CSharpCommandLineParser.Default.Parse(
            args, baseDir, sdkDirectory: null, additionalReferenceDirectories: null);
        return (parsed.CompilationOptions, parsed.ParseOptions);
    }

    private static byte[] ReadReferenceBytes(ICompilerCallReader reader, ReferenceData data)
    {
        using var ms = new MemoryStream();
        reader.CopyAssemblyBytes(data.AssemblyData, ms);
        return ms.ToArray();
    }

    internal static bool IsReferenceAssembly(byte[] bytes)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        if (!peReader.HasMetadata) return false;
        var mr = peReader.GetMetadataReader();
        var asm = mr.GetAssemblyDefinition();
        foreach (var h in asm.GetCustomAttributes())
        {
            var ca = mr.GetCustomAttribute(h);
            switch (ca.Constructor.Kind)
            {
                case HandleKind.MemberReference:
                {
                    var ctor = mr.GetMemberReference((MemberReferenceHandle)ca.Constructor);
                    if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
                    var type = mr.GetTypeReference((TypeReferenceHandle)ctor.Parent);
                    if (mr.StringComparer.Equals(type.Name, "ReferenceAssemblyAttribute") &&
                        mr.StringComparer.Equals(type.Namespace, "System.Runtime.CompilerServices"))
                        return true;
                    continue;
                }
                // The attribute type can live in the assembly being inspected (System.Runtime
                // defines it), in which case the ctor is a MethodDefinition, not a MemberReference.
                case HandleKind.MethodDefinition:
                {
                    var md = mr.GetMethodDefinition((MethodDefinitionHandle)ca.Constructor);
                    var type = mr.GetTypeDefinition(md.GetDeclaringType());
                    if (mr.StringComparer.Equals(type.Name, "ReferenceAssemblyAttribute") &&
                        mr.StringComparer.Equals(type.Namespace, "System.Runtime.CompilerServices"))
                        return true;
                    continue;
                }
            }
        }
        return false;
    }

    internal static byte[] RefasmBytes(byte[] input, string displayName)
    {
        try
        {
            using var peReader = new PEReader(new MemoryStream(input, writable: false));
            var mr = peReader.GetMetadataReader();
            var logger = new LoggerBase(new NullRefasmerLogger());
            return MetadataImporter.MakeRefasm(mr, peReader, logger, new AllowPublic(omitNonApiMembers: true), omitNonApiMembers: true, makeMock: false, omitReferenceAssemblyAttr: false);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Refasmer failed to rewrite reference assembly '{displayName}': {ex.Message}", ex);
        }
    }

    private static string ComputeSha256(byte[] bytes)
    {
        return Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();
    }

    private static byte[] BuildTar(byte[] manifestJson, Dictionary<string, byte[]> blobs)
    {
        using var ms = new MemoryStream();
        using (var writer = new TarWriter(ms, TarEntryFormat.Ustar, leaveOpen: true))
        {
            WriteTarEntry(writer, "manifest.json", manifestJson);
            foreach (var kv in blobs.OrderBy(kv => kv.Key, StringComparer.Ordinal))
            {
                WriteTarEntry(writer, $"refs/{kv.Key}.dll", kv.Value);
            }
        }
        return ms.ToArray();
    }

    private static void WriteTarEntry(TarWriter writer, string name, byte[] data)
    {
        var entry = new UstarTarEntry(TarEntryType.RegularFile, name)
        {
            ModificationTime = DeterministicTime,
            Mode = UnixFileMode.UserRead | UnixFileMode.UserWrite |
                   UnixFileMode.GroupRead | UnixFileMode.OtherRead,
            Uid = 0,
            Gid = 0,
            UserName = "",
            GroupName = "",
            DataStream = new MemoryStream(data, writable: false),
        };
        writer.WriteEntry(entry);
    }

    private static void WriteAtomic(string fullOutputPath, byte[] contents)
    {
        var dir = Path.GetDirectoryName(fullOutputPath)!;
        var tempPath = Path.Combine(
            dir, $".{Path.GetFileName(fullOutputPath)}.tmp-{Guid.NewGuid():N}");
        try
        {
            File.WriteAllBytes(tempPath, contents);
            File.Move(tempPath, fullOutputPath, overwrite: true);
        }
        catch
        {
            try { if (File.Exists(tempPath)) File.Delete(tempPath); } catch { }
            throw;
        }
    }

    private sealed class NullRefasmerLogger : ILogger
    {
        public bool IsEnabled(LogLevel level) => false;
        public void Log(LogLevel level, string message) { }
    }
}
