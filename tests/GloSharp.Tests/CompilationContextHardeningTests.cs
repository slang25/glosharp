using System.Formats.Tar;
using System.Security.Cryptography;
using GloSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Tests;

// U-proj F6/F10/F16/F25, R-core #21: complog/.glocontext selection, docs, offline packs, hardening.
public class CompilationContextHardeningTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gs-ctx-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static string Sha(byte[] bytes) => Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private static void WriteGloContext(
        string path, int version, List<ManifestPack>? packs, List<ManifestReference> references,
        Dictionary<string, byte[]>? blobs = null, List<ManifestCompilation>? compilations = null)
    {
        var manifest = new GloContextManifest
        {
            Version = version,
            Packs = packs,
            Compilations = compilations ??
            [
                new ManifestCompilation { ProjectName = "Test", TargetFramework = "net10.0", References = references },
            ],
        };

        using var tarStream = new MemoryStream();
        using (var writer = new TarWriter(tarStream, TarEntryFormat.Ustar, leaveOpen: true))
        {
            writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, "manifest.json")
            {
                DataStream = new MemoryStream(ManifestSerializer.Serialize(manifest)),
            });
            foreach (var (name, bytes) in blobs ?? [])
            {
                writer.WriteEntry(new UstarTarEntry(TarEntryType.RegularFile, $"refs/{name}.dll")
                {
                    DataStream = new MemoryStream(bytes),
                });
            }
        }

        var compressed = ZstdSharpCodec.Instance.Compress(tarStream.ToArray(), 3, 27);
        var output = new byte[GloContextFormat.HeaderSize + compressed.Length];
        GloContextFormat.WriteHeader(output, (byte)version);
        compressed.CopyTo(output, GloContextFormat.HeaderSize);
        File.WriteAllBytes(path, output);
    }

    private static string? DocsOf(IEnumerable<MetadataReference> references, string metadataName)
    {
        var compilation = CSharpCompilation.Create("probe", [CSharpSyntaxTree.ParseText("class C {}")], references);
        return compilation.GetTypeByMetadataName(metadataName)?.GetDocumentationCommentXml();
    }

    // ---------- selection (U-proj F10, F25) ----------

    private sealed record Comp(string Name, string Tfm);

    private static readonly Comp[] MultiTarget = [new("Lib", "net10.0"), new("Lib", "net8.0"), new("App", "net10.0")];

    private static Comp Select(string? project, string? tfm, List<string> warnings) =>
        CompilationSelector.Select(MultiTarget, c => c.Name, c => c.Tfm, project, tfm, "complog", warnings);

    [Test]
    public async Task Select_NoSelector_PicksFirst_WithWarningListingCandidates()
    {
        var warnings = new List<string>();
        var selected = Select(null, null, warnings);

        await Assert.That(selected).IsEqualTo(MultiTarget[0]);
        await Assert.That(warnings.Single()).Contains("Lib (net10.0), Lib (net8.0), App (net10.0)");
        await Assert.That(warnings.Single()).Contains("--complog-project");
    }

    [Test]
    [Arguments("Lib", "net8.0")]
    [Arguments("lib.csproj", "NET8.0")]
    public async Task Select_ByNameAndFramework(string project, string tfm)
    {
        var warnings = new List<string>();
        var selected = Select(project, tfm, warnings);

        await Assert.That(selected).IsEqualTo(MultiTarget[1]);
        await Assert.That(warnings.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments("Lib (net8.0)")]
    [Arguments("Lib(net8.0)")]
    [Arguments("Lib.csproj (net8.0)")]
    public async Task Select_NameWithFrameworkSelector(string project)
    {
        var warnings = new List<string>();
        await Assert.That(Select(project, "net10.0", warnings)).IsEqualTo(MultiTarget[1]);
        await Assert.That(warnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Select_MultiTargetWithoutFramework_WarnsToPassFramework()
    {
        var warnings = new List<string>();
        var selected = Select("Lib", null, warnings);

        await Assert.That(selected).IsEqualTo(MultiTarget[0]);
        await Assert.That(warnings.Single()).Contains("--framework");
    }

    [Test]
    public async Task Select_UnknownFrameworkForMultiTarget_Throws()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Select("Lib", "net9.0", []));
        await Assert.That(ex.Message).Contains("net9.0");
        await Assert.That(ex.Message).Contains("Lib (net8.0)");
    }

    [Test]
    public async Task Select_FrameworkDiffersFromSingleTargetProject_WarnsInsteadOfFailing()
    {
        var warnings = new List<string>();
        var selected = Select("App", "net8.0", warnings);

        await Assert.That(selected).IsEqualTo(MultiTarget[2]);
        await Assert.That(warnings.Single()).Contains("net8.0");
    }

    [Test]
    public async Task Select_UnknownProject_ListsNameAndFramework()
    {
        var ex = Assert.Throws<InvalidOperationException>(() => Select("Nope", null, []));
        await Assert.That(ex.Message).Contains("Lib (net8.0)");
    }

    [Test]
    public async Task ComplogResolver_SelectsByCsprojName_AndWarnsWhenImplicit()
    {
        var complog = ComplogFixture.GetOrBuildMultiProjectComplog();
        using var resolver = CompilationContextResolverFactory.Open(complog);

        var implicitPick = resolver.Resolve();
        var explicitPick = resolver.Resolve("ProjB.csproj");
        var withTfm = resolver.Resolve("ProjB (net8.0)");

        await Assert.That(implicitPick.Warnings.Single()).Contains("ProjA (net8.0)");
        await Assert.That(implicitPick.Warnings.Single()).Contains("ProjB (net8.0)");
        await Assert.That(explicitPick.ProjectName).IsEqualTo("ProjB");
        await Assert.That(explicitPick.Warnings.Count).IsEqualTo(0);
        await Assert.That(withTfm.ProjectName).IsEqualTo("ProjB");
    }

    // ---------- XML docs for BCL hovers (U-proj F6) ----------

    [Test]
    public async Task ComplogResolver_AttachesBclXmlDocs()
    {
        var complog = ComplogFixture.GetOrBuildMultiProjectComplog();
        using var resolver = CompilationContextResolverFactory.Open(complog);

        var result = resolver.Resolve("ProjA");

        await Assert.That(DocsOf(result.References, "System.Console")).Contains("<summary>");
    }

    [Test]
    public async Task GloContextResolver_PointerReferences_CarryBclXmlDocs()
    {
        var complog = ComplogFixture.GetOrBuildMultiProjectComplog();
        var dir = NewTempDir();
        try
        {
            var path = Path.Combine(dir, "ctx.glocontext");
            var compaction = ComplogCompactor.Compact(complog, path, new ComplogCompactionOptions());
            if (compaction.PointersCreated == 0)
                return; // canonical packs unavailable on this machine; nothing to check

            using var resolver = CompilationContextResolverFactory.Open(path);
            var result = resolver.Resolve("ProjA");

            await Assert.That(DocsOf(result.References, "System.Console")).Contains("<summary>");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------- offline: verified installed-SDK packs (U-proj F16) ----------

    private static (string PackRoot, byte[] Dll) MakePack(string sourcesRoot, string source, string? xml = null)
    {
        var dll = GloContextPointerTests.CompileAssembly(source, "Widget");
        var refDir = Path.Combine(sourcesRoot, "microsoft.netcore.app.ref", "10.0.9", "ref", "net10.0");
        Directory.CreateDirectory(refDir);
        File.WriteAllBytes(Path.Combine(refDir, "Widget.dll"), dll);
        if (xml != null)
            File.WriteAllText(Path.Combine(refDir, "Widget.xml"), xml);
        return (Path.Combine(sourcesRoot, "microsoft.netcore.app.ref", "10.0.9"), dll);
    }

    [Test]
    public async Task GloContextResolver_SkipsPackCopyThatFailsVerification_AndUsesNextSource()
    {
        var dir = NewTempDir();
        try
        {
            // First source has a different build of the pack (like a source-built SDK).
            MakePack(Path.Combine(dir, "a"), "namespace W; public class Widget { public int Other; }");
            var (goodRoot, _) = MakePack(Path.Combine(dir, "b"),
                "namespace W; public class Widget { }",
                """<?xml version="1.0"?><doc><members><member name="T:W.Widget"><summary>Canonical widget.</summary></member></members></doc>""");
            var (hash, _) = PackContentHasher.HashRefDlls(goodRoot);

            var ctx = Path.Combine(dir, "w.glocontext");
            WriteGloContext(ctx, 2,
                [new ManifestPack { Id = "microsoft.netcore.app.ref", Version = "10.0.9", Sha256 = hash }],
                [new ManifestReference { Pack = 0, Path = "ref/net10.0/Widget.dll", Display = "Widget.dll" }]);

            var packResolver = new ReferencePackResolver(
            [
                new DirectoryPackSource(Path.Combine(dir, "a"), "non-canonical"),
                new DirectoryPackSource(Path.Combine(dir, "b"), "canonical"),
            ]);

            using var resolver = GloContextResolver.Open(ctx, packResolver);
            var result = resolver.Resolve();

            await Assert.That(DocsOf(result.References, "W.Widget")).Contains("Canonical widget.");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task InstalledSdkPackSource_MatchesSdkCasing()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "packs", "Microsoft.NETCore.App.Ref", "10.0.9", "ref", "net10.0"));
            var source = new InstalledSdkPackSource(dir);

            var located = source.TryLocate(new PackIdentity("microsoft.netcore.app.ref", "10.0.9"));

            await Assert.That(located).IsNotNull();
            await Assert.That(string.Equals(
                located, Path.Combine(dir, "packs", "Microsoft.NETCore.App.Ref", "10.0.9"),
                StringComparison.OrdinalIgnoreCase)).IsTrue();
            await Assert.That(source.TryLocate(new PackIdentity("microsoft.netcore.app.ref", "10.0.8"))).IsNull();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task CreateForReading_FindsPacksShippedWithTheInstalledSdk_WithoutNetwork()
    {
        var dotnetRoot = FrameworkResolver.FindDotnetRoot();
        await Assert.That(dotnetRoot).IsNotNull();
        var packDir = Path.Combine(dotnetRoot!, "packs", "Microsoft.NETCore.App.Ref");
        var version = Path.GetFileName(Directory.GetDirectories(packDir).First());

        var resolver = ReferencePackResolver.CreateForReading(allowDownload: false);

        await Assert.That(resolver.TryLocate(new PackIdentity("Microsoft.NETCore.App.Ref", version))).IsNotNull();
    }

    // ---------- hardening (R-core #21, U-proj F26) ----------

    [Test]
    public async Task GloContextResolver_BlobWhoseBytesDontMatchItsHash_Throws()
    {
        var dir = NewTempDir();
        try
        {
            var dll = GloContextPointerTests.CompileAssembly("public class Lib {}", "Lib");
            var wrongName = Sha([1, 2, 3]);
            var ctx = Path.Combine(dir, "tampered.glocontext");
            WriteGloContext(ctx, 1, null,
                [new ManifestReference { Blob = wrongName, Display = "Lib.dll" }],
                new Dictionary<string, byte[]> { [wrongName] = dll });

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctx));
            await Assert.That(ex.Message).Contains("corrupt");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task GloContextResolver_BlobWithMatchingHash_Loads()
    {
        var dir = NewTempDir();
        try
        {
            var dll = GloContextPointerTests.CompileAssembly("public class Lib {}", "Lib");
            var ctx = Path.Combine(dir, "ok.glocontext");
            WriteGloContext(ctx, 1, null,
                [new ManifestReference { Blob = Sha(dll), Display = "Lib.dll" }],
                new Dictionary<string, byte[]> { [Sha(dll)] = dll });

            using var resolver = GloContextResolver.Open(ctx);
            await Assert.That(resolver.Resolve().References.Count).IsEqualTo(1);
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task GloContextResolver_TruncatedFile_SaysTruncatedOrCorrupt()
    {
        var dir = NewTempDir();
        try
        {
            var dll = GloContextPointerTests.CompileAssembly("public class Lib {}", "Lib");
            var ctx = Path.Combine(dir, "t.glocontext");
            WriteGloContext(ctx, 1, null,
                [new ManifestReference { Blob = Sha(dll), Display = "Lib.dll" }],
                new Dictionary<string, byte[]> { [Sha(dll)] = dll });
            var bytes = File.ReadAllBytes(ctx);
            File.WriteAllBytes(ctx, bytes[..(bytes.Length / 2)]);

            var ex = Assert.Throws<InvalidDataException>(() => GloContextResolver.Open(ctx));
            await Assert.That(ex.Message).Contains("truncated or corrupt");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task ZstdDecompress_RefusesPayloadsOverTheCap()
    {
        var compressed = ZstdSharpCodec.Instance.Compress(new byte[1_000_000], 3, 20);

        var ex = Assert.Throws<InvalidDataException>(() =>
            ZstdSharpCodec.Instance.Decompress(compressed, maxDecompressedSize: 1000));
        await Assert.That(ex.Message).Contains("limit");

        await Assert.That(ZstdSharpCodec.Instance.Decompress(compressed).Length).IsEqualTo(1_000_000);
    }

    [Test]
    public async Task Compact_GloContextInput_ExplainsItIsAlreadyCompacted()
    {
        var dir = NewTempDir();
        try
        {
            var dll = GloContextPointerTests.CompileAssembly("public class Lib {}", "Lib");
            var ctx = Path.Combine(dir, "in.glocontext");
            WriteGloContext(ctx, 1, null,
                [new ManifestReference { Blob = Sha(dll), Display = "Lib.dll" }],
                new Dictionary<string, byte[]> { [Sha(dll)] = dll });

            var ex = Assert.Throws<InvalidDataException>(() =>
                ComplogCompactor.Compact(ctx, Path.Combine(dir, "out.glocontext"), new ComplogCompactionOptions()));
            await Assert.That(ex.Message).Contains("already a .glocontext");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }
}
