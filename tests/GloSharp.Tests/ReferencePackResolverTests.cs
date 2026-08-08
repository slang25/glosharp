using GloSharp.Core;

namespace GloSharp.Tests;

public class ReferencePackResolverTests
{
    private static string MakePackDir(string root, string id, string version, params string[] relativeDllPaths)
    {
        var packDir = Path.Combine(root, id, version);
        foreach (var rel in relativeDllPaths)
        {
            var full = Path.Combine(packDir, rel.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(full)!);
            File.WriteAllBytes(full, new byte[] { 0x4D, 0x5A });
        }
        return packDir;
    }

    [Test]
    public async Task TryLocate_FirstSourceWins()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
        try
        {
            var rootA = Path.Combine(tempDir, "a");
            var rootB = Path.Combine(tempDir, "b");
            var dirA = MakePackDir(rootA, "microsoft.netcore.app.ref", "10.0.9", "ref/net10.0/System.Runtime.dll");
            MakePackDir(rootB, "microsoft.netcore.app.ref", "10.0.9", "ref/net10.0/System.Runtime.dll");

            var resolver = new ReferencePackResolver(new IPackSource[]
            {
                new DirectoryPackSource(rootA, "source A"),
                new DirectoryPackSource(rootB, "source B"),
            });

            var located = resolver.TryLocate(new PackIdentity("Microsoft.NETCore.App.Ref", "10.0.9"));
            await Assert.That(located).IsEqualTo(dirA);
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task TryLocate_FallsThroughToLaterSource()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
        try
        {
            var rootA = Path.Combine(tempDir, "a");
            var rootB = Path.Combine(tempDir, "b");
            Directory.CreateDirectory(rootA);
            var dirB = MakePackDir(rootB, "microsoft.aspnetcore.app.ref", "10.0.9", "ref/net10.0/Microsoft.AspNetCore.dll");

            var resolver = new ReferencePackResolver(new IPackSource[]
            {
                new DirectoryPackSource(rootA, "source A"),
                new DirectoryPackSource(rootB, "source B"),
            });

            var located = resolver.TryLocate(new PackIdentity("microsoft.aspnetcore.app.ref", "10.0.9"));
            await Assert.That(located).IsEqualTo(dirB);
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Locate_Miss_EnumeratesEveryLocationAndRemedies()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
        try
        {
            var rootA = Path.Combine(tempDir, "a");
            var rootB = Path.Combine(tempDir, "b");
            var resolver = new ReferencePackResolver(new IPackSource[]
            {
                new DirectoryPackSource(rootA, "NuGet global packages folder"),
                new DirectoryPackSource(rootB, "glosharp pack cache"),
            });

            var ex = Assert.Throws<InvalidOperationException>(() =>
                resolver.Locate(new PackIdentity("microsoft.netcore.app.ref", "10.0.9")));

            await Assert.That(ex.Message).Contains("microsoft.netcore.app.ref/10.0.9");
            await Assert.That(ex.Message).Contains(rootA);
            await Assert.That(ex.Message).Contains(rootB);
            await Assert.That(ex.Message).Contains("--self-contained");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task PackIdentity_NormalizesToLowercase()
    {
        var a = new PackIdentity("Microsoft.NETCore.App.Ref", "10.0.9");
        var b = new PackIdentity("microsoft.netcore.app.ref", "10.0.9");
        await Assert.That(a).IsEqualTo(b);
        await Assert.That(a.ToString()).IsEqualTo("microsoft.netcore.app.ref/10.0.9");
    }

    [Test]
    public async Task NuGetDownloadSource_UnreachableFeed_ReturnsNullAndLeavesNoPartialCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
        try
        {
            var cacheRoot = Path.Combine(tempDir, "cache");
            // Port 1 on localhost refuses connections immediately — no network dependency.
            var source = new NuGetDownloadSource(cacheRoot, "http://127.0.0.1:1/v3-flatcontainer");
            var pack = new PackIdentity("microsoft.netcore.app.ref", "10.0.9");

            var located = source.TryLocate(pack);

            await Assert.That(located).IsNull();
            var packDir = Path.Combine(cacheRoot, pack.Id);
            var leftovers = Directory.Exists(packDir)
                ? Directory.EnumerateFileSystemEntries(packDir).ToList()
                : new List<string>();
            await Assert.That(leftovers.Count).IsEqualTo(0);
            await Assert.That(source.Describe(pack)).Contains("nuget.org download");
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task DirectoryPackSource_RequiresRefDirectory()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
        try
        {
            // A pack directory without a ref/ subdir is not a usable targeting pack.
            var packDir = Path.Combine(tempDir, "microsoft.netcore.app.ref", "10.0.9");
            Directory.CreateDirectory(packDir);

            var source = new DirectoryPackSource(tempDir, "test");
            await Assert.That(source.TryLocate(new PackIdentity("microsoft.netcore.app.ref", "10.0.9"))).IsNull();
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }
}
