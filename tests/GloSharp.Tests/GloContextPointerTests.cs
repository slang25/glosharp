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
        => WriteGloContext(path, GloContextFormat.Version2, 2, packs, references, blobs);

    /// <summary>
    /// Builds a .glocontext with the header and manifest versions set independently, so
    /// tests can produce the mismatched/downgraded files a well-behaved writer never emits.
    /// </summary>
    private static void WriteGloContext(
        string path, byte headerVersion, int manifestVersion,
        List<ManifestPack>? packs, List<ManifestReference> references,
        Dictionary<string, byte[]>? blobs = null)
    {
        var manifest = new GloContextManifest
        {
            Version = manifestVersion,
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
        GloContextFormat.WriteHeader(output, headerVersion);
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

    [Test]
    public async Task CanonicalPackIndex_RepeatedAssemblyAcrossTfms_DisambiguatesByOrigin()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"canon-{Guid.NewGuid():N}");
        try
        {
            // The same file name and assembly version under two tfm directories, with
            // different bytes and MVIDs — only the origin path says which one is meant.
            const string sourceA = """
                [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
                public class Widget { public int Value { get; set; } }
                """;
            const string sourceB = """
                [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
                public class Widget { public int Value { get; set; } public int Other { get; set; } }
                """;
            var net9 = CompileAssembly(sourceA, "TestLib");
            var net10 = CompileAssembly(sourceB, "TestLib");

            var packRoot = Path.Combine(tempDir, "pack");
            foreach (var (tfm, bytes) in new[] { ("net9.0", net9), ("net10.0", net10) })
            {
                var dll = Path.Combine(packRoot, "ref", tfm, "TestLib.dll");
                Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
                File.WriteAllBytes(dll, bytes);
            }

            var index = CanonicalPackIndex.Build(packRoot);
            var other = CompileAssembly(
                """
                [assembly: System.Reflection.AssemblyVersion("1.2.3.4")]
                public class Widget { public int Third { get; set; } }
                """, "TestLib");

            // Name+version is ambiguous across the two tfms: the origin path resolves it…
            var byOrigin = index.Match(other, ReadMvid(other), "TestLib.dll", "ref/net9.0/TestLib.dll");
            await Assert.That(byOrigin).IsNotNull();
            await Assert.That(byOrigin!.RelativePath).IsEqualTo("ref/net9.0/TestLib.dll");

            // …and without one it is no match at all, so the reference gets embedded.
            await Assert.That(index.Match(other, ReadMvid(other), "TestLib.dll")).IsNull();

            // An exact-byte match still lands on the file the reference actually came from.
            var byShaWithOrigin = index.Match(net10, ReadMvid(net10), "TestLib.dll", "ref/net10.0/TestLib.dll");
            await Assert.That(byShaWithOrigin!.RelativePath).IsEqualTo("ref/net10.0/TestLib.dll");
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
            var packDir = Path.Combine(packsRoot, "microsoft.netcore.app.ref", "10.0.9");
            var dll = Path.Combine(packDir, "ref", "net10.0", "Widget.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
            File.WriteAllBytes(dll, lib);

            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    new()
                    {
                        Id = "microsoft.netcore.app.ref",
                        Version = "10.0.9",
                        Sha256 = PackContentHasher.HashRefDlls(packDir).ContentHash,
                    },
                },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
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
                new List<ManifestPack>
                {
                    new()
                    {
                        Id = "microsoft.netcore.app.ref",
                        Version = "10.0.9",
                        Sha256 = new string('0', 64),
                    },
                },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
                        Display = "Widget.dll",
                    },
                });

            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(packsRoot, "test packs") });
            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath, packResolver));
            await Assert.That(ex.Message).Contains("Content hash mismatch");
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
                new List<ManifestPack>
                {
                    new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9", Sha256 = new string('0', 64) },
                },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
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
    public async Task Resolver_V2Pointer_FileMissingFromVerifiedPack_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2gone-{Guid.NewGuid():N}");
        try
        {
            var lib = CompileAssembly("public class Widget { }", "Widget");
            var packsRoot = Path.Combine(tempDir, "packs");
            var packDir = Path.Combine(packsRoot, "microsoft.netcore.app.ref", "10.0.9");
            var dll = Path.Combine(packDir, "ref", "net10.0", "Widget.dll");
            Directory.CreateDirectory(Path.GetDirectoryName(dll)!);
            File.WriteAllBytes(dll, lib);

            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    new()
                    {
                        Id = "microsoft.netcore.app.ref",
                        Version = "10.0.9",
                        Sha256 = PackContentHasher.HashRefDlls(packDir).ContentHash,
                    },
                },
                new List<ManifestReference>
                {
                    // Points at a file that is not part of the (correctly hashed) pack contents.
                    new()
                    {
                        Pack = 0,
                        Path = "ref/net10.0/Other.dll",
                        Display = "Other.dll",
                    },
                });

            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(packsRoot, "test packs") });
            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath, packResolver));
            await Assert.That(ex.Message).Contains("missing file 'ref/net10.0/Other.dll'");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_V2Pack_WithoutContentHash_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2nohash-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9" },
                },
                new List<ManifestReference>
                {
                    new() { Pack = 0, Path = "ref/net10.0/Widget.dll", Display = "Widget.dll" },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("missing a valid content hash");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task PackContentHasher_IsDeterministicAndOrderIndependent()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"pch-{Guid.NewGuid():N}");
        try
        {
            var a = CompileAssembly("public class A { }", "A");
            var b = CompileAssembly("public class B { }", "B");

            var pack1 = Path.Combine(tempDir, "pack1");
            var pack2 = Path.Combine(tempDir, "pack2");
            foreach (var (root, order) in new[] { (pack1, new[] { "A", "B" }), (pack2, new[] { "B", "A" }) })
            {
                Directory.CreateDirectory(Path.Combine(root, "ref", "net10.0"));
                foreach (var name in order)
                    File.WriteAllBytes(
                        Path.Combine(root, "ref", "net10.0", $"{name}.dll"), name == "A" ? a : b);
            }

            var h1 = PackContentHasher.HashRefDlls(pack1).ContentHash;
            var h2 = PackContentHasher.HashRefDlls(pack2).ContentHash;
            await Assert.That(h1).IsEqualTo(h2);
            await Assert.That(h1.Length).IsEqualTo(64);

            // Changing any file's bytes changes the hash.
            File.WriteAllBytes(Path.Combine(pack2, "ref", "net10.0", "A.dll"), b);
            await Assert.That(PackContentHasher.HashRefDlls(pack2).ContentHash).IsNotEqualTo(h1);
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
                new List<ManifestPack>
                {
                    new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9", Sha256 = new string('0', 64) },
                },
                new List<ManifestReference>
                {
                    new()
                    {
                        Blob = new string('a', 64),
                        Pack = 0,
                        Path = "ref/net10.0/Widget.dll",
                        Display = "Widget.dll",
                    },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("exactly one of");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_HeaderAndManifestVersionMismatch_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2vermix-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteGloContext(
                ctxPath, GloContextFormat.Version2, manifestVersion: 1,
                packs: null,
                references: new List<ManifestReference>
                {
                    new() { Blob = new string('a', 64), Display = "Lib.dll" },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("does not match manifest version");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_V1FileDeclaringPointers_RejectedWithoutAcquiringPacks()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v1ptr-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);

            // A v1 file promises to be self-contained; pointers in one would smuggle pack
            // acquisition past readers that trust the version byte.
            var withPacks = Path.Combine(tempDir, "packs.glocontext");
            WriteGloContext(
                withPacks, GloContextFormat.Version1, manifestVersion: 1,
                packs: new List<ManifestPack>
                {
                    new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9", Sha256 = new string('0', 64) },
                },
                references: new List<ManifestReference>
                {
                    new() { Pack = 0, Path = "ref/net10.0/Widget.dll", Display = "Widget.dll" },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(withPacks));
            await Assert.That(ex.Message).Contains("must be self-contained");

            var packAllOnly = Path.Combine(tempDir, "packall.glocontext");
            WriteGloContext(
                packAllOnly, GloContextFormat.Version1, manifestVersion: 1,
                packs: null,
                references: new List<ManifestReference> { new() { PackAll = 0, Tfm = "net10.0" } });

            var ex2 = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(packAllOnly));
            await Assert.That(ex2.Message).Contains("outside the packs array");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_PackIdentityTraversal_Rejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"v2packtrav-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    // Would resolve to <packages root>/../../../etc without validation.
                    new() { Id = "..", Version = "..", Sha256 = new string('0', 64) },
                },
                new List<ManifestReference>
                {
                    new() { Pack = 0, Path = "ref/net10.0/Widget.dll", Display = "Widget.dll" },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("invalid id or version");
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
                new List<ManifestPack>
                {
                    new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9", Sha256 = new string('0', 64) },
                },
                new List<ManifestReference>
                {
                    new()
                    {
                        Pack = 0,
                        Path = "ref/../../../etc/evil.dll",
                        Display = "evil.dll",
                    },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("invalid path");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    // ---------- Whole-pack (packAll) references ----------

    private static (List<PackIdentity> Order, Dictionary<PackIdentity, CanonicalPackIndex?> Indexes, string PackDir)
        BuildFakePack(string tempDir, params string[] dllNames)
    {
        var packDir = Path.Combine(tempDir, "microsoft.netcore.app.ref", "10.0.9");
        Directory.CreateDirectory(Path.Combine(packDir, "ref", "net10.0"));
        foreach (var name in dllNames)
        {
            var lib = CompileAssembly($"public class {name.Replace(".", "")} {{ }}", name);
            File.WriteAllBytes(Path.Combine(packDir, "ref", "net10.0", $"{name}.dll"), lib);
        }
        var identity = new PackIdentity("microsoft.netcore.app.ref", "10.0.9");
        var indexes = new Dictionary<PackIdentity, CanonicalPackIndex?>
        {
            [identity] = CanonicalPackIndex.Build(packDir),
        };
        return (new List<PackIdentity> { identity }, indexes, packDir);
    }

    private static ManifestReference Pointer(int pack, string path) =>
        new() { Pack = pack, Path = path, Display = path[(path.LastIndexOf('/') + 1)..] };

    [Test]
    public async Task Collapse_FullCoverage_BecomesSinglePackAllEntry()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"collapse-{Guid.NewGuid():N}");
        try
        {
            var (order, indexes, _) = BuildFakePack(tempDir, "Alpha", "Beta");
            var refs = new List<ManifestReference>
            {
                Pointer(0, "ref/net10.0/Alpha.dll"),
                Pointer(0, "ref/net10.0/Beta.dll"),
                new() { Blob = new string('a', 64), Display = "Lib.dll" },
            };

            var collapsed = ComplogCompactor.CollapseWholePackReferences(refs, order, indexes);

            await Assert.That(collapsed.Count).IsEqualTo(2);
            await Assert.That(collapsed[0].PackAll).IsEqualTo(0);
            await Assert.That(collapsed[0].Tfm).IsEqualTo("net10.0");
            await Assert.That(collapsed[1].Blob).IsNotNull();
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Collapse_PartialCoverage_KeepsExplicitPointers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"collapse-{Guid.NewGuid():N}");
        try
        {
            var (order, indexes, _) = BuildFakePack(tempDir, "Alpha", "Beta");
            var refs = new List<ManifestReference> { Pointer(0, "ref/net10.0/Alpha.dll") };

            var collapsed = ComplogCompactor.CollapseWholePackReferences(refs, order, indexes);

            await Assert.That(collapsed.Count).IsEqualTo(1);
            await Assert.That(collapsed[0].IsPointer).IsTrue();
            await Assert.That(collapsed[0].IsPackAll).IsFalse();
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Collapse_AliasedReference_KeepsExplicitPointers()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"collapse-{Guid.NewGuid():N}");
        try
        {
            var (order, indexes, _) = BuildFakePack(tempDir, "Alpha", "Beta");
            var refs = new List<ManifestReference>
            {
                Pointer(0, "ref/net10.0/Alpha.dll"),
                new()
                {
                    Pack = 0,
                    Path = "ref/net10.0/Beta.dll",
                    Display = "Beta.dll",
                    Aliases = new List<string> { "beta" },
                },
            };

            var collapsed = ComplogCompactor.CollapseWholePackReferences(refs, order, indexes);

            await Assert.That(collapsed.Count).IsEqualTo(2);
            await Assert.That(collapsed.All(r => r.IsPointer)).IsTrue();
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_PackAll_ExpandsToSortedPackContents()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packall-{Guid.NewGuid():N}");
        try
        {
            var packsRoot = Path.Combine(tempDir, "packs");
            var (_, _, packDir) = BuildFakePack(packsRoot, "Zeta", "Alpha");

            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    new()
                    {
                        Id = "microsoft.netcore.app.ref",
                        Version = "10.0.9",
                        Sha256 = PackContentHasher.HashRefDlls(packDir).ContentHash,
                    },
                },
                new List<ManifestReference>
                {
                    new() { PackAll = 0, Tfm = "net10.0" },
                });

            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(packsRoot, "test packs") });
            using var resolver = GloContextResolver.Open(ctxPath, packResolver);
            var result = resolver.Resolve();

            await Assert.That(result.References.Count).IsEqualTo(2);
            await Assert.That(result.References[0].Display).Contains("Alpha.dll");
            await Assert.That(result.References[1].Display).Contains("Zeta.dll");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_PackAll_UnknownTfm_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packall-{Guid.NewGuid():N}");
        try
        {
            var packsRoot = Path.Combine(tempDir, "packs");
            var (_, _, packDir) = BuildFakePack(packsRoot, "Alpha");

            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    new()
                    {
                        Id = "microsoft.netcore.app.ref",
                        Version = "10.0.9",
                        Sha256 = PackContentHasher.HashRefDlls(packDir).ContentHash,
                    },
                },
                new List<ManifestReference>
                {
                    new() { PackAll = 0, Tfm = "net9.0" },
                });

            var packResolver = new ReferencePackResolver(
                new IPackSource[] { new DirectoryPackSource(packsRoot, "test packs") });
            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath, packResolver));
            await Assert.That(ex.Message).Contains("no ref assemblies under 'ref/net9.0/'");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Resolver_MalformedReference_PointerAndPackAll_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packallbad-{Guid.NewGuid():N}");
        try
        {
            Directory.CreateDirectory(tempDir);
            var ctxPath = Path.Combine(tempDir, "test.glocontext");
            WriteV2GloContext(
                ctxPath,
                new List<ManifestPack>
                {
                    new() { Id = "microsoft.netcore.app.ref", Version = "10.0.9", Sha256 = new string('0', 64) },
                },
                new List<ManifestReference>
                {
                    new() { Pack = 0, Path = "ref/net10.0/A.dll", PackAll = 0, Tfm = "net10.0" },
                });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctxPath));
            await Assert.That(ex.Message).Contains("exactly one of");
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
