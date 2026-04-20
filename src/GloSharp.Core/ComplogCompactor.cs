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

        var blobs = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var mvidToBlobHash = new Dictionary<Guid, string>();
        var manifest = new GloContextManifest { Version = 1 };

        int referencesBefore = 0;
        int refasmRewrittenCount = 0;
        int analyzersDropped = 0;
        int originalSourcesDropped = 0;
        int generatedSourcesDropped = 0;

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

            foreach (var refData in references)
            {
                if (!mvidToBlobHash.TryGetValue(refData.Mvid, out var hash))
                {
                    var bytes = ReadReferenceBytes(reader, refData);

                    if (options.RewriteReferences && !IsReferenceAssembly(bytes))
                    {
                        bytes = RefasmBytes(bytes, refData.FileName);
                        refasmRewrittenCount++;
                    }

                    hash = ComputeSha256(bytes);
                    mvidToBlobHash[refData.Mvid] = hash;

                    if (!blobs.ContainsKey(hash))
                        blobs[hash] = bytes;
                }

                manifestCompilation.References.Add(new ManifestReference
                {
                    Blob = hash,
                    Display = refData.FileName,
                    Aliases = refData.Aliases.ToList(),
                    EmbedInteropTypes = refData.EmbedInteropTypes,
                });
            }

            manifest.Compilations.Add(manifestCompilation);
        }

        var manifestJson = ManifestSerializer.Serialize(manifest);
        var tarBytes = BuildTar(manifestJson, blobs);
        var compressed = ZstdSharpCodec.Instance.Compress(
            tarBytes, options.ZstdLevel, options.ZstdWindowLog);

        var output = new byte[GloContextFormat.HeaderSize + compressed.Length];
        GloContextFormat.WriteHeader(output);
        compressed.CopyTo(output, GloContextFormat.HeaderSize);

        WriteAtomic(fullOutputPath, output);

        return new ComplogCompactionResult
        {
            InputSizeBytes = inputSize,
            OutputSizeBytes = output.Length,
            ReferencesBefore = referencesBefore,
            ReferencesAfter = blobs.Count,
            RefasmRewrittenCount = refasmRewrittenCount,
            AnalyzersDropped = analyzersDropped,
            OriginalSourcesDropped = originalSourcesDropped,
            GeneratedSourcesDropped = generatedSourcesDropped,
        };
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

    private static bool IsReferenceAssembly(byte[] bytes)
    {
        using var peReader = new PEReader(new MemoryStream(bytes, writable: false));
        if (!peReader.HasMetadata) return false;
        var mr = peReader.GetMetadataReader();
        var asm = mr.GetAssemblyDefinition();
        foreach (var h in asm.GetCustomAttributes())
        {
            var ca = mr.GetCustomAttribute(h);
            if (ca.Constructor.Kind != HandleKind.MemberReference) continue;
            var ctor = mr.GetMemberReference((MemberReferenceHandle)ca.Constructor);
            if (ctor.Parent.Kind != HandleKind.TypeReference) continue;
            var type = mr.GetTypeReference((TypeReferenceHandle)ctor.Parent);
            if (mr.StringComparer.Equals(type.Name, "ReferenceAssemblyAttribute") &&
                mr.StringComparer.Equals(type.Namespace, "System.Runtime.CompilerServices"))
                return true;
        }
        return false;
    }

    private static byte[] RefasmBytes(byte[] input, string displayName)
    {
        try
        {
            using var peReader = new PEReader(new MemoryStream(input, writable: false));
            var mr = peReader.GetMetadataReader();
            var logger = new LoggerBase(new NullRefasmerLogger());
            return MetadataImporter.MakeRefasm(mr, peReader, logger, new AllowPublic(omitNonApiMembers: false), omitNonApiMembers: false, makeMock: false, omitReferenceAssemblyAttr: false);
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
