using System.Collections.Concurrent;
using GloSharp.Core;
using Microsoft.CodeAnalysis;

namespace GloSharp.Tests;

/// <summary>
/// Preprocessor symbols, complog-derived compilation options, meta accuracy and the context cache.
/// </summary>
public class CompilationOptionsTests
{
    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task Process_TfmPreprocessorSymbols_AreDefined()
    {
        var source = "#if NET8_0_OR_GREATER\nConsole.WriteLine(\"modern\");\n#else\nthis does not compile\n#endif";
        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions { TargetFramework = "net8.0" });

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Hovers.Any(h => h.TargetText == "WriteLine")).IsTrue();
    }

    [Test]
    public async Task TargetFrameworkSymbols_Net8()
    {
        var symbols = TargetFrameworkSymbols.Get("net8.0");

        string[] expected = ["NET", "NET8_0", "NET5_0_OR_GREATER", "NET8_0_OR_GREATER", "NETCOREAPP", "NETCOREAPP3_1_OR_GREATER", "TRACE"];
        foreach (var symbol in expected)
            await Assert.That(symbols).Contains(symbol);
        await Assert.That(symbols).DoesNotContain("NET9_0_OR_GREATER");
        await Assert.That(symbols).DoesNotContain("NETSTANDARD");
    }

    [Test]
    public async Task TargetFrameworkSymbols_OtherFamilies()
    {
        await Assert.That(TargetFrameworkSymbols.Get("netstandard2.0")).Contains("NETSTANDARD2_0_OR_GREATER");
        await Assert.That(TargetFrameworkSymbols.Get("netstandard2.0")).DoesNotContain("NETSTANDARD2_1_OR_GREATER");
        await Assert.That(TargetFrameworkSymbols.Get("net48")).Contains("NETFRAMEWORK");
        await Assert.That(TargetFrameworkSymbols.Get("net48")).Contains("NET472_OR_GREATER");
        await Assert.That(TargetFrameworkSymbols.Get("net10.0-windows")).Contains("WINDOWS");
        await Assert.That(TargetFrameworkSymbols.Get("net10.0-windows")).Contains("NET10_0_OR_GREATER");
    }

    [Test]
    public async Task Process_Complog_InheritsProjectCompilerOptions()
    {
        var complog = ComplogFixture.GetOrBuildMultiProjectComplog();
        var source = """
            #if GLOSHARP_FIXTURE_B
            unsafe { int* p = null; }
            #else
            this does not compile
            #endif
            int unused = 1;
            """;

        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions
        {
            ComplogPath = complog,
            ComplogProject = "ProjB",
        });

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        // AllowUnsafeBlocks from the project
        await Assert.That(result.Errors.Any(e => e.Code == "CS0227")).IsFalse();
        // NoWarn CS0219 from the project
        await Assert.That(result.Errors.Any(e => e.Code == "CS0219")).IsFalse();
        // Effective settings come from the complog
        await Assert.That(result.Meta.Nullable).IsEqualTo("enable");
        await Assert.That(result.Meta.TargetFramework).IsEqualTo("net8.0");
    }

    [Test]
    public async Task Process_Complog_MarkerOverridesStillApply()
    {
        var complog = ComplogFixture.GetOrBuildMultiProjectComplog();
        var source = "// @nullable: disable\nstring s = null;\nConsole.WriteLine(s);";

        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions
        {
            ComplogPath = complog,
            ComplogProject = "ProjB",
        });

        await Assert.That(result.Meta.Nullable).IsEqualTo("disable");
        await Assert.That(result.Errors.Any(e => e.Code == "CS8600")).IsFalse();
    }

    [Test]
    public async Task Process_Meta_ReportsEffectiveConfigValues()
    {
        var result = await _processor.ProcessAsync("var x = 1;", new GloSharpProcessorOptions
        {
            LangVersion = "11",
            Nullable = "disable",
        });

        await Assert.That(result.Meta.LangVersion).IsEqualTo("11");
        await Assert.That(result.Meta.Nullable).IsEqualTo("disable");
        await Assert.That(result.Meta.Warnings).IsNotNull();
        await Assert.That(result.Meta.Warnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_LangVersion14_IsAccepted()
    {
        var result = await _processor.ProcessAsync("// @langVersion: 14\nvar x = 1;");

        await Assert.That(result.Errors.Any(e => e.Code == "GS0001")).IsFalse();
        await Assert.That(result.Meta.LangVersion).IsEqualTo("14");
    }

    [Test]
    public async Task Process_Hidden_IsPopulatedFromHiddenRanges()
    {
        var source = "var a = 1;\n// ---cut-start---\nvar b = 2;\n// ---cut-end---\nvar c = a + b;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hidden.Count).IsEqualTo(1);
        await Assert.That(result.Hidden[0].Line).IsEqualTo(1);
        await Assert.That(result.Hidden[0].SourceStartLine).IsEqualTo(1);
        await Assert.That(result.Hidden[0].SourceEndLine).IsEqualTo(3);
    }

    [Test]
    public async Task ContextCache_ConcurrentCallers_ShareOneFactoryCall()
    {
        var cache = new CompilationContextCache();
        var calls = 0;

        var results = new ConcurrentBag<List<MetadataReference>>();
        await Task.WhenAll(Enumerable.Range(0, 32).Select(_ => Task.Run(() =>
        {
            results.Add(cache.GetOrAdd("key", () =>
            {
                Interlocked.Increment(ref calls);
                Thread.Sleep(20);
                return new List<MetadataReference>();
            }));
        })));

        await Assert.That(calls).IsEqualTo(1);
        await Assert.That(results.Distinct().Count()).IsEqualTo(1);
    }

    [Test]
    public async Task ContextCache_FailedFactory_IsRetried()
    {
        var cache = new CompilationContextCache();
        await Assert.That(() => cache.GetOrAdd<string>("k", () => throw new InvalidOperationException("boom")))
            .Throws<InvalidOperationException>();

        await Assert.That(cache.GetOrAdd<string>("k", () => "ok")).IsEqualTo("ok");
    }

    [Test]
    public async Task ComplogPackageInference_ReadsNuGetPaths()
    {
        var refs = new[]
        {
            MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray<byte>.Empty, filePath: "/home/u/.nuget/packages/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll"),
            MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray<byte>.Empty, filePath: "/usr/share/dotnet/packs/Microsoft.NETCore.App.Ref/8.0.0/ref/net8.0/System.Runtime.dll"),
            MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray<byte>.Empty, filePath: "/home/u/.nuget/packages/microsoft.aspnetcore.app.ref/8.0.0/ref/net8.0/Microsoft.AspNetCore.dll"),
            MetadataReference.CreateFromImage(System.Collections.Immutable.ImmutableArray<byte>.Empty, filePath: "/src/Other/obj/Debug/net8.0/ref/Other.dll"),
        };

        var packages = ComplogPackageInference.InferPackages(refs);

        await Assert.That(packages.Count).IsEqualTo(1);
        await Assert.That(packages[0].Name).IsEqualTo("newtonsoft.json");
        await Assert.That(packages[0].Version).IsEqualTo("13.0.3");
        await Assert.That(ComplogPackageInference.ReferencesAspNetCore(refs)).IsTrue();
    }
}
