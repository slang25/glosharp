using TwoHash.Core;

namespace TwoHash.Tests;

public class TwohashProcessorTests
{
    private readonly TwohashProcessor _processor = new();

    [Test]
    public async Task Process_LocalVariable_ExtractsTypeInfo()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("int");
        await Assert.That(result.Hovers[0].Text).Contains("x");
        await Assert.That(result.Hovers[0].SymbolKind).IsEqualTo("Local");
        await Assert.That(result.Hovers[0].TargetText).IsEqualTo("x");
    }

    [Test]
    public async Task Process_StringVariable_ShowsType()
    {
        var source = "var greeting = \"hello\";\n//      ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("string");
        await Assert.That(result.Hovers[0].Text).Contains("greeting");
    }

    [Test]
    public async Task Process_MethodWithOverloads_ShowsOverloadCount()
    {
        var source = "Console.WriteLine(\"test\");\n//        ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("overloads");
        await Assert.That(result.Hovers[0].OverloadCount).IsNotNull();
        await Assert.That(result.Hovers[0].OverloadCount!.Value).IsGreaterThan(1);
    }

    [Test]
    public async Task Process_StructuredParts_ContainsCorrectKinds()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var parts = result.Hovers[0].Parts;
        await Assert.That(parts.Any(p => p.Kind == "keyword" && p.Text == "int")).IsTrue();
        await Assert.That(parts.Any(p => p.Kind == "localName" && p.Text == "x")).IsTrue();
        await Assert.That(parts.Any(p => p.Kind == "text" && p.Text == "local variable")).IsTrue();
    }

    [Test]
    public async Task Process_CleanCompilation_Succeeds()
    {
        var source = "// @noErrors\nvar x = 42;\nConsole.WriteLine(x);";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_ExpectedError_MarksAsExpected()
    {
        var source = "// @errors: CS0103\nConsole.WriteLine(undeclared);";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Errors.Any(e => e.Code == "CS0103" && e.Expected)).IsTrue();
    }

    [Test]
    public async Task Process_CutMarker_HidesSetupCode()
    {
        var source = "using System.Text;\nvar sb = new StringBuilder();\n// ---cut---\nsb.Append(\"hello\");\n//   ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Code).DoesNotContain("StringBuilder()");
        await Assert.That(result.Code).Contains("sb.Append");
        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("Append");
    }

    [Test]
    public async Task Process_GenericType_ShowsFullType()
    {
        var source = "var list = new List<int> { 1, 2, 3 };\n//    ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("List<int>");
    }

    [Test]
    public async Task Process_OutputFormat_HasRequiredFields()
    {
        var source = "var x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Lang).IsEqualTo("csharp");
        await Assert.That(result.Code).IsNotNull();
        await Assert.That(result.Original).IsNotNull();
        await Assert.That(result.Hovers).IsNotNull();
        await Assert.That(result.Errors).IsNotNull();
        await Assert.That(result.Completions).IsNotNull();
        await Assert.That(result.Highlights).IsNotNull();
        await Assert.That(result.Hidden).IsNotNull();
        await Assert.That(result.Meta).IsNotNull();
        await Assert.That(result.Meta.TargetFramework).IsNotNull();
    }

    [Test]
    public async Task Process_JsonSerialization_UseCamelCase()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);
        var json = JsonOutput.Serialize(result);

        await Assert.That(json).Contains("\"code\":");
        await Assert.That(json).Contains("\"hovers\":");
        await Assert.That(json).Contains("\"symbolKind\":");
        await Assert.That(json).Contains("\"targetFramework\":");
        await Assert.That(json).Contains("\"compileSucceeded\":");
        // Should not contain PascalCase
        await Assert.That(json).DoesNotContain("\"Code\":");
        await Assert.That(json).DoesNotContain("\"Hovers\":");
    }

    [Test]
    public async Task Process_EmptyArraysNotNull()
    {
        var source = "var x = 42;";
        var result = await _processor.ProcessAsync(source);
        var json = JsonOutput.Serialize(result);

        await Assert.That(json).Contains("\"completions\": []");
        await Assert.That(json).Contains("\"highlights\": []");
        await Assert.That(json).Contains("\"hidden\": []");
    }

    [Test]
    public async Task Process_WithProjectContext_ResolvesNuGetTypes()
    {
        var source = """
            using Newtonsoft.Json;
            var json = JsonConvert.SerializeObject(new { Name = "test" });
            //                    ^?
            """;

        var fixtureDir = Path.Combine(
            AppContext.BaseDirectory, "..", "..", "..", "fixtures", "sample-project");

        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            ProjectPath = fixtureDir,
        });

        await Assert.That(result.Hovers.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("SerializeObject");
        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Meta.Packages.Count).IsGreaterThan(0);
        await Assert.That(result.Meta.Packages.Any(p => p.Name == "Newtonsoft.Json")).IsTrue();
    }

    [Test]
    public async Task Process_HighlightDirective_PopulatesHighlights()
    {
        var source = "// @highlight\nvar x = 42;\nvar y = 10;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Line).IsEqualTo(0);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("highlight");
        await Assert.That(result.Highlights[0].Character).IsEqualTo(0);
        await Assert.That(result.Highlights[0].Length).IsEqualTo("var x = 42;".Length);
    }

    [Test]
    public async Task Process_DiffDirectives_PopulatesHighlights()
    {
        var source = "// @diff: +\nvar added = 1;\n// @diff: -\nvar removed = 2;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(2);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("add");
        await Assert.That(result.Highlights[1].Kind).IsEqualTo("remove");
    }

    [Test]
    public async Task Process_FocusDirective_PopulatesHighlights()
    {
        var source = "var a = 1;\n// @focus\nvar b = 2;\nvar c = 3;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("focus");
        await Assert.That(result.Highlights[0].Line).IsEqualTo(1); // processed line 1 (var b = 2;)
    }

    [Test]
    public async Task Process_HighlightWithHover_BothPopulated()
    {
        var source = "// @highlight\nvar x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Code).IsEqualTo("var x = 42;");
    }

    // === File-based app directive tests ===

    [Test]
    public async Task Process_FileDirectives_StrippedFromOutput()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\nvar x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Code).IsEqualTo("var x = 42;");
        await Assert.That(result.Code).DoesNotContain("#:package");
    }

    [Test]
    public async Task Process_FileDirectives_PreservedInOriginal()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Original).Contains("#:package Newtonsoft.Json@13.0.3");
    }

    [Test]
    public async Task Process_FileDirectives_MetaPackagesPopulated()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.Packages.Count).IsEqualTo(1);
        await Assert.That(result.Meta.Packages[0].Name).IsEqualTo("Newtonsoft.Json");
        await Assert.That(result.Meta.Packages[0].Version).IsEqualTo("13.0.3");
    }

    [Test]
    public async Task Process_FileDirectives_MetaSdkPopulated()
    {
        var source = "#:sdk Microsoft.NET.Sdk.Web\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.Sdk).IsEqualTo("Microsoft.NET.Sdk.Web");
    }

    [Test]
    public async Task Process_FileDirectives_MetaSdkNullWhenAbsent()
    {
        var source = "var x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.Sdk).IsNull();
    }

    [Test]
    public async Task Process_FileDirectives_HoverPositionsCorrect()
    {
        // With 2 directive lines stripped, hover should still target the right line
        var source = "#:package A@1.0\n#:package B@2.0\nvar x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Line).IsEqualTo(0); // First line of processed output
        await Assert.That(result.Hovers[0].Text).Contains("int");
    }

    [Test]
    public async Task Process_FileBasedApp_ResolvesNuGetTypes()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();
        if (version == null || version.Major < 10)
            return; // Skip if .NET 10+ not available

        var tempDir = Path.Combine(Path.GetTempPath(), $"twohash-e2e-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.cs");

        try
        {
            var source = "#:package Newtonsoft.Json@13.0.3\nusing Newtonsoft.Json;\nvar json = JsonConvert.SerializeObject(new { Name = \"test\" });\n//                    ^?";
            File.WriteAllText(filePath, source);

            var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
            {
                SourceFilePath = filePath,
            });

            await Assert.That(result.Hovers.Count).IsGreaterThanOrEqualTo(1);
            await Assert.That(result.Hovers[0].Text).Contains("SerializeObject");
            await Assert.That(result.Meta.CompileSucceeded).IsTrue();
            await Assert.That(result.Meta.Packages.Count).IsGreaterThan(0);
            await Assert.That(result.Meta.Packages.Any(p => p.Name == "Newtonsoft.Json")).IsTrue();
            await Assert.That(result.Code).DoesNotContain("#:package");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // === Language version and nullable control tests ===

    [Test]
    public async Task Process_LangVersion7_RejectsModernFeatures()
    {
        // Tuple deconstruction is C# 7+ but collection expressions are C# 12+
        var source = "// @langVersion: 7\n// @errors: CS8652\nvar x = [1, 2, 3];";
        var result = await _processor.ProcessAsync(source);

        // Should have diagnostics for unsupported features
        await Assert.That(result.Errors.Count).IsGreaterThan(0);
    }

    [Test]
    public async Task Process_NullableDisable_NoNullableWarnings()
    {
        var source = "// @nullable: disable\nstring s = null;\nConsole.WriteLine(s);";
        var result = await _processor.ProcessAsync(source);

        // With nullable disabled, assigning null to string should not produce CS8600
        var nullableWarnings = result.Errors.Where(e => e.Code == "CS8600").ToList();
        await Assert.That(nullableWarnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_NullableEnable_ProducesWarnings()
    {
        var source = "// @nullable: enable\n// @errors: CS8600\nstring s = null;\nConsole.WriteLine(s);";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Errors.Any(e => e.Code == "CS8600")).IsTrue();
    }

    [Test]
    public async Task Process_DefaultBehavior_UnchangedWithoutMarkers()
    {
        // Default is Latest + Enable — this should work the same as before
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("int");
        await Assert.That(result.Meta.LangVersion).IsNull();
        await Assert.That(result.Meta.Nullable).IsNull();
    }

    [Test]
    public async Task Process_InvalidLangVersion_ProducesError()
    {
        var source = "// @langVersion: 99\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Errors.Count).IsGreaterThan(0);
        await Assert.That(result.Errors[0].Code).IsEqualTo("TH0001");
        await Assert.That(result.Errors[0].Message).Contains("99");
        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
    }

    [Test]
    public async Task Process_InvalidNullable_ProducesError()
    {
        var source = "// @nullable: sometimes\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Errors.Count).IsGreaterThan(0);
        await Assert.That(result.Errors[0].Code).IsEqualTo("TH0002");
        await Assert.That(result.Errors[0].Message).Contains("sometimes");
        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
    }

    [Test]
    public async Task Process_LangVersion_AppearsInMeta()
    {
        var source = "// @langVersion: 12\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.LangVersion).IsEqualTo("12");
    }

    [Test]
    public async Task Process_Nullable_AppearsInMeta()
    {
        var source = "// @nullable: disable\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.Nullable).IsEqualTo("disable");
    }

    [Test]
    public async Task Process_LangVersionAndNullable_OmittedFromJsonWhenAbsent()
    {
        var source = "var x = 42;";
        var result = await _processor.ProcessAsync(source);
        var json = JsonOutput.Serialize(result);

        await Assert.That(json).DoesNotContain("\"langVersion\"");
        await Assert.That(json).DoesNotContain("\"nullable\"");
    }

    [Test]
    public async Task Process_LangVersionAndNullable_PresentInJsonWhenSet()
    {
        var source = "// @langVersion: 12\n// @nullable: disable\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);
        var json = JsonOutput.Serialize(result);

        await Assert.That(json).Contains("\"langVersion\": \"12\"");
        await Assert.That(json).Contains("\"nullable\": \"disable\"");
    }
}
