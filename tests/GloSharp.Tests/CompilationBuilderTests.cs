using GloSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Tests;

/// <summary>
/// <see cref="RequestedSettings"/> (marker vs config, GS0001/GS0002) and
/// <see cref="CompilationBuilder"/> (defaults from the context, global usings).
/// </summary>
public class CompilationBuilderTests
{
    private static Snippet Prepare(string source) => Snippet.Prepare(source, regionName: null);

    private static CompilationContext Context(
        LanguageVersion langVersion = LanguageVersion.Latest,
        NullableContextOptions nullable = NullableContextOptions.Enable,
        bool isWeb = false) => new()
    {
        References = [],
        TargetFramework = "net8.0",
        Packages = [],
        IsWeb = isWeb,
        ParseOptions = new CSharpParseOptions(langVersion, preprocessorSymbols: ["NET8_0"]),
        CompilationOptions = new CSharpCompilationOptions(OutputKind.ConsoleApplication).WithNullableContextOptions(nullable),
    };

    private static readonly RequestedSettings NothingRequested = new(null, null, []);

    // --- RequestedSettings ---

    [Test]
    public async Task Requested_ConfigValues_AreMapped()
    {
        var requested = RequestedSettings.Resolve(Prepare("var x = 1;"), "11", "disable");

        await Assert.That(requested.LangVersion).IsEqualTo(LanguageVersion.CSharp11);
        await Assert.That(requested.Nullable).IsEqualTo(NullableContextOptions.Disable);
        await Assert.That(requested.Errors).IsEmpty();
    }

    [Test]
    public async Task Requested_MarkerBeatsConfig()
    {
        var requested = RequestedSettings.Resolve(
            Prepare("// @langVersion: 10\n// @nullable: warnings\nvar x = 1;"), "12", "disable");

        await Assert.That(requested.LangVersion).IsEqualTo(LanguageVersion.CSharp10);
        await Assert.That(requested.Nullable).IsEqualTo(NullableContextOptions.Warnings);
    }

    [Test]
    public async Task Requested_InvalidMarker_FallsBackToConfig_AndReportsTheMarkersSourceLine()
    {
        var requested = RequestedSettings.Resolve(
            Prepare("#:property Foo=Bar\n// @langVersion: banana\nvar x = 1;"), "12", null);

        await Assert.That(requested.LangVersion).IsEqualTo(LanguageVersion.CSharp12);
        await Assert.That(requested.Errors.Count).IsEqualTo(1);
        var error = requested.Errors[0];
        await Assert.That(error.Code).IsEqualTo(GloSharpDiagnosticCodes.InvalidLangVersion);
        await Assert.That(error.Message).StartsWith("Invalid language version 'banana'. Valid values: ");
        await Assert.That(error.Line).IsEqualTo(0);
        await Assert.That(error.SourceLine).IsEqualTo(1);
    }

    [Test]
    public async Task Requested_InvalidConfig_IsReportedAtLineZero()
    {
        var requested = RequestedSettings.Resolve(Prepare("var x = 1;"), null, "sometimes");

        await Assert.That(requested.Nullable).IsNull();
        await Assert.That(requested.Errors.Count).IsEqualTo(1);
        await Assert.That(requested.Errors[0].Code).IsEqualTo(GloSharpDiagnosticCodes.InvalidNullable);
        await Assert.That(requested.Errors[0].Message).StartsWith("Invalid nullable context 'sometimes'.");
        await Assert.That(requested.Errors[0].SourceLine).IsEqualTo(0);
    }

    [Test]
    public async Task Requested_InvalidConfigAndMarker_ReportsBoth()
    {
        var requested = RequestedSettings.Resolve(Prepare("// @langVersion: nope\nvar x = 1;"), "bad", null);

        await Assert.That(requested.LangVersion).IsNull();
        await Assert.That(requested.Errors.Select(e => e.SourceLine)).IsEquivalentTo(new int?[] { 0, 0 });
        await Assert.That(requested.Errors.Count).IsEqualTo(2);
    }

    // --- CompilationBuilder ---

    [Test]
    public async Task Build_NothingRequested_UsesTheContextsDefaults()
    {
        var built = CompilationBuilder.Build(
            Prepare("var x = 1;"), Context(LanguageVersion.CSharp9, NullableContextOptions.Annotations), NothingRequested, null);

        await Assert.That(built.LangVersion).IsEqualTo(LanguageVersion.CSharp9);
        await Assert.That(built.Nullable).IsEqualTo(NullableContextOptions.Annotations);
        await Assert.That(((CSharpParseOptions)built.Tree.Options).SpecifiedLanguageVersion).IsEqualTo(LanguageVersion.CSharp9);
        await Assert.That(built.Compilation.Options.NullableContextOptions).IsEqualTo(NullableContextOptions.Annotations);
    }

    [Test]
    public async Task Build_RequestedSettings_OverrideTheContext_AndKeepItsSymbols()
    {
        var requested = new RequestedSettings(LanguageVersion.CSharp11, NullableContextOptions.Disable, []);
        var built = CompilationBuilder.Build(Prepare("var x = 1;"), Context(), requested, null);

        await Assert.That(built.LangVersion).IsEqualTo(LanguageVersion.CSharp11);
        await Assert.That(built.Nullable).IsEqualTo(NullableContextOptions.Disable);
        await Assert.That(built.Tree.Options.PreprocessorSymbolNames).Contains("NET8_0");
        await Assert.That(built.Compilation.SyntaxTrees.All(t => t.Options == built.Tree.Options)).IsTrue();
    }

    [Test]
    public async Task Build_CompilesTheCompilationCode_WithoutMarkerLines()
    {
        var snippet = Prepare("var x = 1;\n//  ^?\nConsole.WriteLine(x);");
        var built = CompilationBuilder.Build(snippet, Context(), NothingRequested, null);

        await Assert.That(built.Tree.ToString()).IsEqualTo(snippet.Markers.CompilationCode);
        await Assert.That(built.Tree.ToString()).DoesNotContain("^?");
        await Assert.That(built.Compilation.SyntaxTrees.Count()).IsEqualTo(2);
    }

    [Test]
    public async Task GlobalUsings_DefaultsPlusWebSdk_OrConfigReplacingBoth()
    {
        var plain = CompilationBuilder.BuildGlobalUsings(null, isWeb: false);
        var web = CompilationBuilder.BuildGlobalUsings(null, isWeb: true);
        var configured = CompilationBuilder.BuildGlobalUsings(["System", "", "System", "MyLib"], isWeb: true);

        await Assert.That(plain).Contains("global using System.Linq;");
        await Assert.That(plain).DoesNotContain("Microsoft.AspNetCore");
        await Assert.That(web).Contains("global using System.Linq;");
        await Assert.That(web).Contains("global using Microsoft.AspNetCore.Builder;");
        await Assert.That(configured).IsEqualTo("global using System;\nglobal using MyLib;");
    }
}
