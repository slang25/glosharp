using GloSharp.Core;

namespace GloSharp.Tests;

public class ComplogCompactorTests
{
    private static readonly string FixtureDir = Path.Combine(
        Path.GetDirectoryName(typeof(ComplogCompactorTests).Assembly.Location)!,
        "..", "..", "..", "..", "..", ".context", "complog-experiments");

    private static string? TryFixture(string name)
    {
        var path = Path.GetFullPath(Path.Combine(FixtureDir, name));
        return File.Exists(path) ? path : null;
    }

    [Test]
    public void Compact_MissingInput_ThrowsFileNotFound()
    {
        var outTemp = Path.Combine(Path.GetTempPath(), $"out-{Guid.NewGuid():N}.glocontext");
        Assert.Throws<FileNotFoundException>(() =>
            ComplogCompactor.Compact("/nonexistent/input.complog", outTemp, new ComplogCompactionOptions()));
    }

    [Test]
    public void Compact_UnwritableOutputDir_ThrowsIO()
    {
        var fixture = TryFixture("BclOnly.complog");
        if (fixture == null) return;

        var badPath = "/nonexistent-dir-that-does-not-exist/out.glocontext";
        Assert.Throws<IOException>(() =>
            ComplogCompactor.Compact(fixture, badPath, new ComplogCompactionOptions()));
    }

    [Test]
    public async Task Compact_SameInputTwice_ProducesByteIdenticalOutput()
    {
        var fixture = TryFixture("BclOnly.complog");
        if (fixture == null) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-det-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var out1 = Path.Combine(tempDir, "a.glocontext");
            var out2 = Path.Combine(tempDir, "b.glocontext");
            var options = new ComplogCompactionOptions();

            ComplogCompactor.Compact(fixture, out1, options);
            ComplogCompactor.Compact(fixture, out2, options);

            var bytes1 = await File.ReadAllBytesAsync(out1);
            var bytes2 = await File.ReadAllBytesAsync(out2);

            await Assert.That(bytes1.Length).IsEqualTo(bytes2.Length);
            await Assert.That(bytes1.AsSpan().SequenceEqual(bytes2)).IsTrue();
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Compact_ProducesValidGloContextFile()
    {
        var fixture = TryFixture("BclOnly.complog");
        if (fixture == null) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-valid-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var outPath = Path.Combine(tempDir, "out.glocontext");
            var result = ComplogCompactor.Compact(fixture, outPath, new ComplogCompactionOptions());

            await Assert.That(File.Exists(outPath)).IsTrue();
            await Assert.That(result.OutputSizeBytes).IsEqualTo(new FileInfo(outPath).Length);
            await Assert.That(result.OutputSizeBytes).IsLessThan(result.InputSizeBytes);
            await Assert.That(result.ReferencesBefore).IsGreaterThan(0);

            var header = new byte[GloContextFormat.HeaderSize];
            using (var fs = File.OpenRead(outPath))
                _ = fs.Read(header);
            await Assert.That(GloContextFormat.LooksLikeGloContext(header)).IsTrue();
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Compact_ThenResolve_RoundTripsReferencesAndOptions()
    {
        var fixture = TryFixture("BclOnly.complog");
        if (fixture == null) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-rt-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var outPath = Path.Combine(tempDir, "out.glocontext");
            ComplogCompactor.Compact(fixture, outPath, new ComplogCompactionOptions());

            using var resolver = GloContextResolver.Open(outPath);
            var resolved = resolver.Resolve();

            await Assert.That(resolved.References.Count).IsGreaterThan(0);
            await Assert.That(resolved.TargetFramework).IsNotNull();
            await Assert.That(resolved.CompilationOptions).IsNotNull();
            await Assert.That(resolved.ParseOptions).IsNotNull();
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Compact_AspNetFixture_UnderThreeMegabytes()
    {
        var fixture = TryFixture("AspNet.complog");
        if (fixture == null) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-size-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var outPath = Path.Combine(tempDir, "out.glocontext");
            var result = ComplogCompactor.Compact(fixture, outPath, new ComplogCompactionOptions());
            await Assert.That(result.OutputSizeBytes).IsLessThan(3L * 1024 * 1024);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Compact_MultiProject_DedupesSharedReferences()
    {
        var input = ComplogFixture.GetOrBuildMultiProjectComplog();

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-dedup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var outPath = Path.Combine(tempDir, "out.glocontext");
            var result = ComplogCompactor.Compact(input, outPath, new ComplogCompactionOptions());

            await Assert.That(result.ReferencesBefore).IsGreaterThan(result.ReferencesAfter);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }

    [Test]
    public async Task Compact_NoRefasm_YieldsLargerOutput()
    {
        var fixture = TryFixture("BclOnly.complog");
        if (fixture == null) return;

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-norefasm-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var withRefasm = Path.Combine(tempDir, "with.glocontext");
            var withoutRefasm = Path.Combine(tempDir, "without.glocontext");
            var r1 = ComplogCompactor.Compact(fixture, withRefasm, new ComplogCompactionOptions());
            var r2 = ComplogCompactor.Compact(fixture, withoutRefasm,
                new ComplogCompactionOptions { RewriteReferences = false });

            await Assert.That(r1.RefasmRewrittenCount).IsGreaterThan(0);
            await Assert.That(r2.RefasmRewrittenCount).IsEqualTo(0);
            await Assert.That(r2.OutputSizeBytes).IsGreaterThan(r1.OutputSizeBytes);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
