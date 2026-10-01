using GloSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Tests;

/// <summary>
/// Which <see cref="ICompilationContextProvider"/> a snippet gets, and what the providers that
/// need no restore or complog produce.
/// </summary>
public class CompilationContextProviderTests
{
    private static readonly Snippet Plain = Snippet.Prepare("var x = 1;", regionName: null);
    private static readonly Snippet WithDirectives = Snippet.Prepare("#:package Humanizer.Core@2.14.1\nvar x = 1;", regionName: null);

    private static ICompilationContextProvider Select(Snippet snippet, GloSharpProcessorOptions options) =>
        CompilationContextProviders.Select(snippet, options, new CompilationContextCache());

    // --- selection ---

    [Test]
    public async Task Select_NoContextInputs_IsFrameworkOnly()
    {
        await Assert.That(Select(Plain, new())).IsTypeOf<FrameworkContextProvider>();
    }

    [Test]
    public async Task Select_Directives_IsFileBasedApp()
    {
        await Assert.That(Select(WithDirectives, new())).IsTypeOf<FileBasedAppContextProvider>();
    }

    [Test]
    public async Task Select_Project_WinsOverDirectives()
    {
        var provider = Select(WithDirectives, new() { ProjectPath = "/nowhere/App.csproj" });
        await Assert.That(provider).IsTypeOf<ProjectAssetsContextProvider>();
    }

    [Test]
    public async Task Select_Complog_WinsOverEverything()
    {
        var provider = Select(WithDirectives, new() { ProjectPath = "/nowhere/App.csproj", ComplogPath = "/nowhere/a.complog" });
        await Assert.That(provider).IsTypeOf<ComplogContextProvider>();
    }

    // --- cache fingerprints (must be cheap and never throw for missing inputs) ---

    [Test]
    public async Task Fingerprints_FrameworkAndFileBasedApp_HaveNone()
    {
        await Assert.That(Select(Plain, new()).GetCacheFingerprints()).IsEmpty();
        await Assert.That(Select(WithDirectives, new()).GetCacheFingerprints()).IsEmpty();
    }

    [Test]
    public async Task Fingerprints_MissingProject_FingerprintsTheExpectedAssetsPath()
    {
        var dir = Path.Combine(Path.GetTempPath(), "glosharp-missing-" + Guid.NewGuid().ToString("N"));
        var fingerprints = Select(Plain, new() { ProjectPath = dir }).GetCacheFingerprints().ToList();

        await Assert.That(fingerprints.Count).IsEqualTo(1);
        await Assert.That(fingerprints[0]).StartsWith("assets:");
        await Assert.That(fingerprints[0]).EndsWith("|missing");
    }

    [Test]
    public async Task Fingerprints_Complog_ChangeWithTheFile()
    {
        var path = Path.GetTempFileName();
        try
        {
            var provider = Select(Plain, new() { ComplogPath = path });
            var before = provider.GetCacheFingerprints().Single();

            File.WriteAllText(path, "changed");
            var after = provider.GetCacheFingerprints().Single();

            await Assert.That(before).StartsWith("complog:");
            await Assert.That(after).IsNotEqualTo(before);
        }
        finally
        {
            File.Delete(path);
        }
    }

    // --- resolution ---

    [Test]
    public async Task Framework_DefaultsAndTfmSymbols()
    {
        var context = Select(Plain, new() { TargetFramework = "net8.0" }).Resolve();

        await Assert.That(context.TargetFramework).IsEqualTo("net8.0");
        await Assert.That(context.IsWeb).IsFalse();
        await Assert.That(context.Packages).IsEmpty();
        await Assert.That(context.Warnings).IsEmpty();
        await Assert.That(context.ParseOptions.SpecifiedLanguageVersion).IsEqualTo(LanguageVersion.Latest);
        await Assert.That(context.ParseOptions.PreprocessorSymbolNames).Contains("NET8_0");
        await Assert.That(context.CompilationOptions.NullableContextOptions).IsEqualTo(NullableContextOptions.Enable);
        await Assert.That(context.CompilationOptions.OutputKind).IsEqualTo(OutputKind.ConsoleApplication);
        await Assert.That(context.References.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Framework_NoTfm_UsesTheDefault()
    {
        var context = Select(Plain, new()).Resolve();
        await Assert.That(context.TargetFramework).IsEqualTo(FrameworkResolver.DefaultTargetFramework);
    }

    [Test]
    public async Task Framework_SharesReferencesThroughTheContextCache()
    {
        var cache = new CompilationContextCache();
        var a = CompilationContextProviders.Select(Plain, new() { TargetFramework = "net8.0" }, cache).Resolve();
        var b = CompilationContextProviders.Select(Plain, new() { TargetFramework = "net8.0" }, cache).Resolve();

        await Assert.That(ReferenceEquals(a.References, b.References)).IsTrue();
    }

    [Test]
    public async Task Complog_ContextAdaptsTheProjectsOptionsToASnippet()
    {
        var resolution = new ComplogResolutionResult
        {
            References = [],
            TargetFramework = "net8.0",
            ParseOptions = new CSharpParseOptions(LanguageVersion.CSharp10, DocumentationMode.None, SourceCodeKind.Script,
                preprocessorSymbols: ["MY_DEFINE"]),
            CompilationOptions = new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary,
                    mainTypeName: "Program", cryptoKeyFile: "key.snk", publicSign: true, allowUnsafe: true)
                .WithNullableContextOptions(NullableContextOptions.Disable),
            Warnings = ["picked one of two projects"],
        };

        var context = ComplogContextProvider.ToContext(resolution);

        // Kept: the project's own language version, nullable context, defines and flags
        await Assert.That(context.ParseOptions.SpecifiedLanguageVersion).IsEqualTo(LanguageVersion.CSharp10);
        await Assert.That(context.ParseOptions.PreprocessorSymbolNames).Contains("MY_DEFINE");
        await Assert.That(context.CompilationOptions.NullableContextOptions).IsEqualTo(NullableContextOptions.Disable);
        await Assert.That(context.CompilationOptions.AllowUnsafe).IsTrue();
        await Assert.That(context.Warnings).Contains("picked one of two projects");
        await Assert.That(context.TargetFramework).IsEqualTo("net8.0");

        // Adapted: a regular top-level-statements console app, with doc comments parsed, unsigned
        await Assert.That(context.ParseOptions.Kind).IsEqualTo(SourceCodeKind.Regular);
        await Assert.That(context.ParseOptions.DocumentationMode).IsEqualTo(DocumentationMode.Parse);
        await Assert.That(context.CompilationOptions.OutputKind).IsEqualTo(OutputKind.ConsoleApplication);
        await Assert.That(context.CompilationOptions.MainTypeName).IsNull();
        await Assert.That(context.CompilationOptions.CryptoKeyFile).IsNull();
        await Assert.That(context.CompilationOptions.PublicSign).IsFalse();
    }

    [Test]
    public async Task Complog_MissingFile_Throws()
    {
        var provider = Select(Plain, new() { ComplogPath = "/nowhere/missing.complog" });
        await Assert.That(() => provider.Resolve()).Throws<FileNotFoundException>();
    }
}
