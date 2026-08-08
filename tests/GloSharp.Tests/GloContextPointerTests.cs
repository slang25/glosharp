using System.Formats.Tar;
using System.Reflection.Metadata;
using System.Security.Cryptography;
using GloSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Tests;

public class GloContextPointerTests
{
    // ---------- helpers ----------

    internal static byte[] CompileAssembly(string source, string assemblyName)
    {
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            new[] { CSharpSyntaxTree.ParseText(source) },
            tpa,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary, deterministic: true));
        using var ms = new MemoryStream();
        var emit = compilation.Emit(ms);
        if (!emit.Success)
            throw new InvalidOperationException(string.Join("\n", emit.Diagnostics));
        return ms.ToArray();
    }

    private static string Sha(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static Guid ReadMvid(byte[] bytes)
    {
        using var pe = new System.Reflection.PortableExecutable.PEReader(new MemoryStream(bytes, writable: false));
        var mr = pe.GetMetadataReader();
        return mr.GetGuid(mr.GetModuleDefinition().Mvid);
    }

    /// <summary>Builds a v2 .glocontext with a single compilation and given references.</summary>
    private static void WriteV2GloContext(
        string path, List<ManifestPack> packs, List<ManifestReference> references,
        Dictionary<string, byte[]>? blobs = null)
    {
        var manifest = new GloContextManifest
        {
            Version = 2,
            Packs = packs,
            Compilations =
            {
                new ManifestCompilation
                {
                    ProjectName = "Test",
                    TargetFramework = "net10.0",
                    References = references,
                },
            },
        };

        using var tarStream = new MemoryStream();
        using (var writer = new TarWriter(tarStream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(ManifestSerializer.Serialize(manifest)),
            });
            foreach (var (hash, bytes) in blobs ?? new Dictionary<string, byte[]>())
            {
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"refs/{hash}.dll")
                {
                    DataStream = new MemoryStream(bytes),
                });
            }
        }

        var compressed = ZstdSharpCodec.Instance.Compress(tarStream.ToArray(), 3, 27);
        var output = new byte[GloContextFormat.HeaderSize + compressed.Length];
        GloContextFormat.WriteHeader(output, GloContextFormat.Version2);
        compressed.CopyTo(output, GloContextFormat.HeaderSize);
        File.WriteAllBytes(path, output);
    }

    // ---------- TryParsePackOrigin ----------

    [Test]
    public async Task TryParsePackOrigin_InstalledSdkLayout()
    {
        var origin = ComplogCompactor.TryParsePackOrigin(
            "/usr/local/share/dotnet/packs/Microsoft.NETCore.App.Ref/10.0.9/ref/net10.0/System.Runtime.dll");
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.Value.Pack).IsEqualTo(new PackIdentity("microsoft.netcore.app.ref", "10.0.9"));
        await Assert.That(origin.Value.RelativePath).IsEqualTo("ref/net10.0/System.Runtime.dll");
    }

    [Test]
    public async Task TryParsePackOrigin_GlobalPackagesLayout()
    {
        var origin = ComplogCompactor.TryParsePackOrigin(
            "/Users/x/.nuget/packages/microsoft.aspnetcore.app.ref/10.0.9/ref/net10.0/Microsoft.AspNetCore.dll");
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.Value.Pack).IsEqualTo(new PackIdentity("microsoft.aspnetcore.app.ref", "10.0.9"));
    }

    [Test]
    public async Task TryParsePackOrigin_WindowsSeparators()
    {
        var origin = ComplogCompactor.TryParsePackOrigin(
            @"C:\Program Files\dotnet\packs\Microsoft.NETCore.App.Ref\8.0.10\ref\net8.0\System.Runtime.dll");
        await Assert.That(origin).IsNotNull();
        await Assert.That(origin!.Value.Pack).IsEqualTo(new PackIdentity("microsoft.netcore.app.ref", "8.0.10"));
        await Assert.That(origin.Value.RelativePath).IsEqualTo("ref/net8.0/System.Runtime.dll");
    }

    [Test]
    public async Task TryParsePackOrigin_RejectsNonPackPaths()
    {
        // A NuGet library path must never be treated as a pack origin, even with a framework-looking name.
        await Assert.That(ComplogCompactor.TryParsePackOrigin(
            "/Users/x/.nuget/packages/dapper/2.1.66/lib/net8.0/Dapper.dll")).IsNull();
        await Assert.That(ComplogCompactor.TryParsePackOrigin(
            "/Users/x/.nuget/packages/evil.package/1.0.0/ref/net10.0/System.Runtime.dll")).IsNull();
        await Assert.That(ComplogCompactor.TryParsePackOrigin("System.Runtime.dll")).IsNull();
        await Assert.That(ComplogCompactor.TryParsePackOrigin(null)).IsNull();
    }

    // ---------- CanonicalPackIndex match tiers ----------

    [Test]
    public async Task CanonicalPackIndex_MatchesByShaThenMvidThenNameVersion()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canon-{Guid.NewGuid():N}");
        try
        {
            const string sourceA = """
                [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
                public class Widget { public int Value { get; set; } }
                """;
            const string sourceB = """
                [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
                public class Widget { public int Value { get; set; } public int Other { get; set; } }
                """;
            var canonical = CompileAssembly(sourceA, "TestLib");
            var rebuilt = CompileAssembly(sourceB, "TestLib");

            var packRoot = Path.Combine(tempDir, "pack");
            var dllPath = Path.Combine(packRoot, "ref", "net10.0", "TestLib.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dllPath)!);
            File.WriteAllBytes(dllPath, canonical);

            var index = CanonicalPackIndex.Build(packRoot);

            // Tier 1: identical bytes.
            var bySha = index.Match(canonical, ReadMvid(canonical), "TestLib.dll");
            await Assert.That(bySha).IsNotNull();
            await Assert.That(bySha!.RelativePath).IsEqualTo("ref/net10.0/TestLib.dll");
            await Assert.That(bySha.Sha256).IsEqualTo(Sha(canonical));

            // Tier 2: different bytes, matching MVID (signing/timestamp-only rebuilds).
            var byMvid = index.Match(rebuilt, ReadMvid(canonical), "TestLib.dll");
            await Assert.That(byMvid).IsNotNull();

            // Tier 3: different bytes and MVID, same file name + assembly version.
            var byName = index.Match(rebuilt, ReadMvid(rebuilt), "TestLib.dll");
            await Assert.That(byName).IsNotNull();

            // No tier matches a different file name.
            var miss = index.Match(rebuilt, ReadMvid(rebuilt), "OtherLib.dll");
            await Assert.That(miss).IsNull();
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    // ---------- Resolver v2 ----------

    [Test]
    public async Task Resolver_V2Pointer_ResolvesAndVerifies()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2res-{Guid.NewGuid():N}");
        try
        {
            var lib = CompileAssembly("public class Widget { }", "Widget");
            var packsRoot = Path.Combine(tempDir, "packs");
            var dll = Path.Combine(packsRoot, "microsoft.netcore.app.ref", "10.0.9", "ref", "net10.0", "Widget.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
            File.WriteAllBytes(dll, lib);

            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack> { new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9" } },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
                        Sha256 = Sha(lib),
                        Display = "Widget.dll",
                    },
                });

            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(packsRoot, "test packs") });
            using var resolver = GloContextResolver.Open(ctxPath, packResolver);
            var result = resolver.Resolve();

            await Assert.That(result.References.Count).IsEqualTo(1);
            await Assert.That(result.References[0].Display).Contains("Widget.dll");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_V2Pointer_HashMismatch_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2mis-{Guid.NewGuid():N}");
        try
        {
            var lib = CompileAssembly("public class Widget { }", "Widget");
            var packsRoot = Path.Combine(tempDir, "packs");
            var dll = Path.Combine(packsRoot, "microsoft.netcore.app.ref", "10.0.9", "ref", "net10.0", "Widget.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
            File.WriteAllBytes(dll, lib);

            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack> { new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9" } },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
                        Sha256 = new string('0', 64),
                        Display = "Widget.dll",
                    },
                });

            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(packsRoot, "test packs") });
            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath, packResolver));
            await Assert.That(ex.Message).Contains("Hash mismatch");
            await Assert.That(ex.Message).Contains("ref/net10.0/Widget.dll");
            await Assert.That(ex.Message).Contains("microsoft.netcore.app.ref/10.0.9");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_V2Pointer_MissingPack_ThrowsWithLocations()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2miss-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack> { new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9" } },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
                        Sha256 = new string('0', 64),
                        Display = "Widget.dll",
                    },
                });

            var emptyRoot = Path.Combine(tempDir, "empty");
            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(emptyRoot, "test packs") });
            var ex = Assert.Throws<InvalidOperationException>(() => GloContextResolver.Open(ctxPath, packResolver));
            await Assert.That(ex.Message).Contains("microsoft.netcore.app.ref/10.0.9");
            await Assert.That(ex.Message).Contains(emptyRoot);
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_MalformedReference_BothBlobAndPointer_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2bad-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack> { new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9" } },
                new List<ManifestReference>
                {
                    new()
                    {
                        Blob = new string('a', 64),
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
                        Sha256 = new string('0', 64),
                        Display = "Widget.dll",
                    },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("exactly one of");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_PointerPathTraversal_Rejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2trav-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack> { new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9" } },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/../../../etc/evil.dll",
                        Sha256 = new string('0', 64),
                        Display = "evil.dll",
                    },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("invalid path");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    // ---------- Refasm regressions ----------

    [Test]
    public async Task RefasmBytes_AssemblyWithAnonymousTypeCachedLambda_Succeeds()
    {
        // LINQ lambdas returning anonymous types are cached in static fields whose
        // signatures reference the anonymous TypeDef — the shape that made Refasmer
        // throw UnknownTypeInSignature on EF Core / FluentAssertions with the old settings.
        const string source = """
            using System.Linq;
            public class C
            {
                public static int M(int[] xs) => xs.Select(x => new { Value = x }).Count();
            }
            """;
        var assembly = CompileAssembly(source, "AnonLib");

        var refasmed = ComplogCompactor.RefasmBytes(assembly, "AnonLib.dll");
        await Assert.That(refasmed.Length).IsGreaterThan(0);
        await Assert.That(refasmed.Length).IsLessThan(assembly.Length + 4096);
    }

    [Test]
    public async Task IsReferenceAssembly_MethodDefDeclaredAttribute_Detected()
    {
        // System.Runtime.dll defines ReferenceAssemblyAttribute itself, so the attribute
        // ctor is a MethodDefinition rather than a MemberReference.
        const string source = """
            [assembly: System.Runtime.CompilerServices.ReferenceAssembly]
            namespace System.Runtime.CompilerServices
            {
                internal sealed class ReferenceAssemblyAttribute : System.Attribute { }
            }
            """;
        var assembly = CompileAssembly(source, "SelfRefAttr");
        await Assert.That(ComplogCompactor.IsReferenceAssembly(assembly)).IsTrue();
    }

    [Test]
    public async Task IsReferenceAssembly_MemberRefDeclaredAttribute_Detected()
    {
        const string source = """
            [assembly: System.Runtime.CompilerServices.ReferenceAssembly]
            public class C { }
            """;
        var assembly = CompileAssembly(source, "ExternRefAttr");
        await Assert.That(ComplogCompactor.IsReferenceAssembly(assembly)).IsTrue();
    }

    [Test]
    public async Task IsReferenceAssembly_NoAttribute_NotDetected()
    {
        var assembly = CompileAssembly("public class C { }", "PlainLib");
        await Assert.That(ComplogCompactor.IsReferenceAssembly(assembly)).IsFalse();
    }
}
