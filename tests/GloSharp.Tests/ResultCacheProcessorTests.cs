using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>
/// Processor-level result cache behaviour: key completeness and early return on a hit.
/// </summary>
public class ResultCacheProcessorTests
{
    private string _cacheDir = null!;
    private string _workDir = null!;

    [Before(Test)]
    public void Setup()
    {
        _cacheDir = Path.Combine(Path.GetTempPath(), "glosharp-rc-" + Guid.NewGuid().ToString("N")[..8]);
        _workDir = Path.Combine(Path.GetTempPath(), "glosharp-rcw-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(_workDir);
    }

    [After(Test)]
    public void Cleanup()
    {
        if (Directory.Exists(_cacheDir)) Directory.Delete(_cacheDir, recursive: true);
        if (Directory.Exists(_workDir)) Directory.Delete(_workDir, recursive: true);
    }

    [Test]
    public async Task Process_DifferentRegions_AreCachedSeparately()
    {
        var source = "#region a\nvar a = 1;\n#endregion\n#region b\nvar b = 2;\n#endregion";
        var processor = new GloSharpProcessor();

        var first = await processor.ProcessAsync(source, new GloSharpProcessorOptions { CacheDir = _cacheDir, RegionName = "a" });
        var second = await processor.ProcessAsync(source, new GloSharpProcessorOptions { CacheDir = _cacheDir, RegionName = "b" });

        await Assert.That(first.Code).IsEqualTo("var a = 1;");
        await Assert.That(second.Code).IsEqualTo("var b = 2;");
    }

    [Test]
    public async Task Process_DifferentNullable_AreCachedSeparately()
    {
        var source = "string s = null;\nConsole.WriteLine(s);";
        var processor = new GloSharpProcessor();

        var enabled = await processor.ProcessAsync(source, new GloSharpProcessorOptions { CacheDir = _cacheDir, Nullable = "enable" });
        var disabled = await processor.ProcessAsync(source, new GloSharpProcessorOptions { CacheDir = _cacheDir, Nullable = "disable" });

        await Assert.That(enabled.Errors.Any(e => e.Code == "CS8600")).IsTrue();
        await Assert.That(disabled.Errors.Any(e => e.Code == "CS8600")).IsFalse();
    }

    [Test]
    public async Task Process_DifferentLangVersion_AreCachedSeparately()
    {
        var source = "int[] x = [1, 2, 3];";
        var processor = new GloSharpProcessor();

        var latest = await processor.ProcessAsync(source, new GloSharpProcessorOptions { CacheDir = _cacheDir });
        var old = await processor.ProcessAsync(source, new GloSharpProcessorOptions { CacheDir = _cacheDir, LangVersion = "11" });

        await Assert.That(latest.Meta.CompileSucceeded).IsTrue();
        await Assert.That(old.Meta.CompileSucceeded).IsFalse();
    }

    [Test]
    public async Task Process_CacheHit_ReturnsCachedResultAndBuildsCompilationLazily()
    {
        var source = "var x = 42;\n//  ^?";
        var options = new GloSharpProcessorOptions { CacheDir = _cacheDir };

        var miss = await new GloSharpProcessor().ProcessWithContextAsync(source, options);
        var hit = await new GloSharpProcessor().ProcessWithContextAsync(source, options);

        await Assert.That(miss.FromCache).IsFalse();
        await Assert.That(hit.FromCache).IsTrue();
        await Assert.That(JsonOutput.Serialize(hit.Result)).IsEqualTo(JsonOutput.Serialize(miss.Result));

        // render still needs a compilation for classification — it is built on demand
        await Assert.That(hit.Compilation.SyntaxTrees.Count()).IsGreaterThan(0);
        await Assert.That(hit.SyntaxTree.ToString()).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Process_ProjectAssetsChange_InvalidatesCache()
    {
        var fixtureAssets = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "fixtures", "sample-project", "obj", "project.assets.json");
        var objDir = Path.Combine(_workDir, "obj");
        Directory.CreateDirectory(objDir);
        var assets = Path.Combine(objDir, "project.assets.json");
        File.Copy(fixtureAssets, assets);

        var source = "var x = 42;";
        var options = new GloSharpProcessorOptions { CacheDir = _cacheDir, ProjectPath = _workDir };

        var first = await new GloSharpProcessor().ProcessWithContextAsync(source, options);
        var second = await new GloSharpProcessor().ProcessWithContextAsync(source, options);

        // Simulate a re-restore (e.g. a package bump) rewriting the assets file
        File.SetLastWriteTimeUtc(assets, DateTime.UtcNow.AddMinutes(5));
        var third = await new GloSharpProcessor().ProcessWithContextAsync(source, options);

        await Assert.That(first.FromCache).IsFalse();
        await Assert.That(second.FromCache).IsTrue();
        await Assert.That(third.FromCache).IsFalse();
    }

    [Test]
    public async Task Process_ComplogChange_InvalidatesCache()
    {
        var complog = Path.Combine(_workDir, "copy.complog");
        File.Copy(ComplogFixture.GetOrBuildMultiProjectComplog(), complog);

        var source = "var x = 42;";
        var options = new GloSharpProcessorOptions { CacheDir = _cacheDir, ComplogPath = complog };

        var first = await new GloSharpProcessor().ProcessWithContextAsync(source, options);
        var second = await new GloSharpProcessor().ProcessWithContextAsync(source, options);
        File.SetLastWriteTimeUtc(complog, DateTime.UtcNow.AddMinutes(5));
        var third = await new GloSharpProcessor().ProcessWithContextAsync(source, options);

        await Assert.That(first.FromCache).IsFalse();
        await Assert.That(second.FromCache).IsTrue();
        await Assert.That(third.FromCache).IsFalse();
    }

    [Test]
    public async Task Process_DifferentComplogProject_AreCachedSeparately()
    {
        var complog = ComplogFixture.GetOrBuildMultiProjectComplog();
        var source = "unsafe { int* p = null; }";
        var processor = new GloSharpProcessor();

        var projA = await processor.ProcessAsync(source, new GloSharpProcessorOptions
        {
            CacheDir = _cacheDir, ComplogPath = complog, ComplogProject = "ProjA",
        });
        var projB = await processor.ProcessAsync(source, new GloSharpProcessorOptions
        {
            CacheDir = _cacheDir, ComplogPath = complog, ComplogProject = "ProjB",
        });

        // Only ProjB sets AllowUnsafeBlocks
        await Assert.That(projA.Errors.Any(e => e.Code == "CS0227")).IsTrue();
        await Assert.That(projB.Errors.Any(e => e.Code == "CS0227")).IsFalse();
    }

    [Test]
    public async Task ComputeKey_IgnoresCacheDir_ButCoversEveryOtherOption()
    {
        var baseline = new GloSharpProcessorOptions();
        var key = ResultCache.ComputeKey("s", baseline);

        await Assert.That(ResultCache.ComputeKey("s", baseline with { CacheDir = "/elsewhere" })).IsEqualTo(key);

        GloSharpProcessorOptions[] variants =
        [
            baseline with { TargetFramework = "net9.0" },
            baseline with { ProjectPath = "p" },
            baseline with { RegionName = "r" },
            baseline with { SourceFilePath = "f.cs" },
            baseline with { NoRestore = true },
            baseline with { ComplogPath = "c.complog" },
            baseline with { ComplogProject = "P" },
            baseline with { ImplicitUsings = ["System"] },
            baseline with { LangVersion = "12" },
            baseline with { Nullable = "disable" },
        ];

        foreach (var variant in variants)
            await Assert.That(ResultCache.ComputeKey("s", variant)).IsNotEqualTo(key);
    }
}
