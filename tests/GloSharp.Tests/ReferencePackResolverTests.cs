using System.IO.Compression;
using System.Net;
using System.Net.Sockets;
using GloSharp.Core;

namespace GloSharp.Tests;

public class ReferencePackResolverTests
{
    /// <summary>
    /// Serves one canned nupkg over loopback, counting requests, so the download path can be
    /// exercised without touching nuget.org.
    /// </summary>
    private sealed class LocalNupkgFeed : IDisposable
    {
        private readonly HttpListener _listener = new();
        private readonly byte[] _nupkg;
        private int _requestCount;

        public LocalNupkgFeed(byte[] nupkg)
        {
            _nupkg = nupkg;
            var port = FreeLoopbackPort();
            BaseUrl = $"http://127.0.0.1:{port}/v3-flatcontainer";
            _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
            _listener.Start();
            _ = Task.Run(ServeAsync);
        }

        public string BaseUrl { get; }
        public int RequestCount => Volatile.Read(ref _requestCount);

        private async Task ServeAsync()
        {
            while (_listener.IsListening)
            {
                HttpListenerContext context;
                try { context = await _listener.GetContextAsync(); }
                catch { return; }

                Interlocked.Increment(ref _requestCount);
                context.Response.ContentLength64 = _nupkg.Length;
                await context.Response.OutputStream.WriteAsync(_nupkg);
                context.Response.Close();
            }
        }

        private static int FreeLoopbackPort()
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();
            return port;
        }

        public void Dispose() => _listener.Close();
    }

    private static byte[] MakeNupkg(params (string EntryName, byte[] Content)[] entries)
    {
        using var ms = new MemoryStream();
        using (var zip = new ZipArchive(ms, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var (name, content) in entries)
            {
                using var stream = zip.CreateEntry(name).Open();
                stream.Write(content);
            }
        }
        return ms.ToArray();
    }

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
    public async Task NuGetDownloadSource_Download_ExtractsRefDllsOnlyAndServesFromCache()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"packs-{Guid.NewGuid():N}");
        var dll = new byte[] { 0x4D, 0x5A, 0x90, 0x00 };
        var nupkg = MakeNupkg(
            ("ref/net10.0/System.Runtime.dll", dll),
            ("ref/net10.0/System.Console.dll", dll),
            ("lib/net10.0/System.Runtime.dll", new byte[] { 0xFF }),
            ("ref/net10.0/System.Runtime.xml", new byte[] { 0xFF }),
            ("microsoft.netcore.app.ref.nuspec", new byte[] { 0xFF }));

        using var feed = new LocalNupkgFeed(nupkg);
        try
        {
            var cacheRoot = Path.Combine(tempDir, "cache");
            var source = new NuGetDownloadSource(cacheRoot, feed.BaseUrl);
            var pack = new PackIdentity("Microsoft.NETCore.App.Ref", "10.0.9");

            var located = source.TryLocate(pack);

            var expected = Path.Combine(cacheRoot, "microsoft.netcore.app.ref", "10.0.9");
            await Assert.That(located).IsEqualTo(expected);
            await Assert.That(feed.RequestCount).IsEqualTo(1);

            // Only ref/**/*.dll is extracted — no lib/, no docs, no nuspec.
            var extracted = Directory.EnumerateFiles(expected, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(expected, f).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal)
                .ToList();
            await Assert.That(extracted).IsEquivalentTo(new[]
            {
                "ref/net10.0/System.Console.dll",
                "ref/net10.0/System.Runtime.dll",
            });

            // The cached pack is reusable without a second request.
            await Assert.That(source.TryLocate(pack)).IsEqualTo(expected);
            await Assert.That(feed.RequestCount).IsEqualTo(1);

            // The staging directory was renamed into place, not left behind.
            var packRoot = Path.Combine(cacheRoot, "microsoft.netcore.app.ref");
            await Assert.That(Directory.EnumerateDirectories(packRoot).ToList())
                .IsEquivalentTo(new[] { expected });
        }
        finally { if (Directory.Exists(tempDir)) Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task PackIdentity_RejectsValuesThatWouldEscapeTheRoot()
    {
        // Pack ids and versions come from untrusted manifests and are combined into
        // cache/package paths, so each must be a single path segment.
        foreach (var bad in new[] { "..", ".", "", "  ", "a/b", @"a\b", "/etc" })
        {
            await Assert.That(PackIdentity.TryCreate("microsoft.netcore.app.ref", bad)).IsNull();
            await Assert.That(PackIdentity.TryCreate(bad, "10.0.9")).IsNull();
            Assert.Throws<ArgumentException>(() => new PackIdentity("microsoft.netcore.app.ref", bad));
        }

        await Assert.That(PackIdentity.TryCreate("Microsoft.NETCore.App.Ref", "10.0.9"))
            .IsEqualTo(new PackIdentity("microsoft.netcore.app.ref", "10.0.9"));
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
