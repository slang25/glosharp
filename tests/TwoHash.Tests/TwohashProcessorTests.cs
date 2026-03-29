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

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("int");
        await Assert.That(persistent.Text).Contains("x");
        await Assert.That(persistent.SymbolKind).IsEqualTo("Local");
        await Assert.That(persistent.TargetText).IsEqualTo("x");
    }

    [Test]
    public async Task Process_StringVariable_ShowsType()
    {
        var source = "var greeting = \"hello\";\n//      ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("string");
        await Assert.That(persistent.Text).Contains("greeting");
    }

    [Test]
    public async Task Process_MethodWithOverloads_ShowsOverloadCount()
    {
        var source = "Console.WriteLine(\"test\");\n//        ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("overloads");
        await Assert.That(persistent.OverloadCount).IsNotNull();
        await Assert.That(persistent.OverloadCount!.Value).IsGreaterThan(1);
    }

    [Test]
    public async Task Process_StructuredParts_ContainsCorrectKinds()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var parts = result.Hovers.First(h => h.Persistent).Parts;
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
        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("Append");
    }

    [Test]
    public async Task Process_GenericType_ShowsFullType()
    {
        var source = "var list = new List<int> { 1, 2, 3 };\n//    ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("List<int>");
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

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("SerializeObject");
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
        await Assert.That(result.Hovers.Any(h => h.Persistent)).IsTrue();
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

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Line).IsEqualTo(0); // First line of processed output
        await Assert.That(persistent.Text).Contains("int");
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

            var persistent = result.Hovers.First(h => h.Persistent);
            await Assert.That(persistent.Text).Contains("SerializeObject");
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

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("int");
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

    // === Richer error display tests ===

    [Test]
    public async Task Process_SingleLineError_OmitsEndLineEndCharacter()
    {
        var source = "// @errors: CS0103\nConsole.WriteLine(undeclared);";
        var result = await _processor.ProcessAsync(source);

        var error = result.Errors.First(e => e.Code == "CS0103");
        await Assert.That(error.EndLine).IsNull();
        await Assert.That(error.EndCharacter).IsNull();
    }

    [Test]
    public async Task Process_SingleLineError_JsonOmitsEndFields()
    {
        var source = "// @errors: CS0103\nConsole.WriteLine(undeclared);";
        var result = await _processor.ProcessAsync(source);
        var json = JsonOutput.Serialize(result);

        await Assert.That(json).DoesNotContain("\"endLine\"");
        await Assert.That(json).DoesNotContain("\"endCharacter\"");
    }

    [Test]
    public async Task Process_WarningDiagnostic_HasWarningSeverity()
    {
        // Nullable enable produces CS8600 warning
        var source = "// @nullable: enable\n// @errors: CS8600\nstring s = null;\nConsole.WriteLine(s);";
        var result = await _processor.ProcessAsync(source);

        var warning = result.Errors.FirstOrDefault(e => e.Code == "CS8600");
        await Assert.That(warning).IsNotNull();
        await Assert.That(warning!.Severity).IsEqualTo("warning");
    }

    // === Auto-hover extraction tests ===

    [Test]
    public async Task Process_AutoHover_ExtractsHoversForIdentifiers()
    {
        var source = "var x = 42;";
        var result = await _processor.ProcessAsync(source);

        // Should have auto-hovers for identifiers (var, x) but not for punctuation/literals
        await Assert.That(result.Hovers.Count).IsGreaterThan(0);
        await Assert.That(result.Hovers.All(h => !h.Persistent)).IsTrue();
        // Should have hover for 'x'
        await Assert.That(result.Hovers.Any(h => h.TargetText == "x")).IsTrue();
        // Should NOT have hover for '42' or ';'
        await Assert.That(result.Hovers.Any(h => h.TargetText == "42")).IsFalse();
        await Assert.That(result.Hovers.Any(h => h.TargetText == ";")).IsFalse();
    }

    [Test]
    public async Task Process_PersistentHover_HasPersistentFlag()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.Where(h => h.Persistent).ToList();
        await Assert.That(persistent.Count).IsEqualTo(1);
        await Assert.That(persistent[0].TargetText).IsEqualTo("x");
    }

    [Test]
    public async Task Process_Deduplication_PersistentTakesPrecedence()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        // Only one hover for 'x' — the persistent one, not both
        var xHovers = result.Hovers.Where(h => h.TargetText == "x").ToList();
        await Assert.That(xHovers.Count).IsEqualTo(1);
        await Assert.That(xHovers[0].Persistent).IsTrue();
    }

    [Test]
    public async Task Process_AutoHover_PositionMappingWithMarkers()
    {
        var source = "// @highlight\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        // 'x' should be on processed line 0 (highlight marker removed)
        var xHover = result.Hovers.First(h => h.TargetText == "x");
        await Assert.That(xHover.Line).IsEqualTo(0);
    }

    [Test]
    public async Task Process_AutoHover_ExcludesHiddenSections()
    {
        var source = "var hidden = 1;\n// ---cut---\nvar visible = 2;";
        var result = await _processor.ProcessAsync(source);

        // Should NOT have hover for 'hidden' (before cut)
        await Assert.That(result.Hovers.Any(h => h.TargetText == "hidden")).IsFalse();
        // Should have hover for 'visible' (after cut)
        await Assert.That(result.Hovers.Any(h => h.TargetText == "visible")).IsTrue();
    }

    // === Keyword hover filtering tests ===

    [Test]
    public async Task Process_KeywordCase_NoHover()
    {
        var source = "void M() { switch (1) { case 1: break; } }";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.TargetText == "case")).IsFalse();
    }

    [Test]
    public async Task Process_KeywordBreak_NoHover()
    {
        var source = "void M() { switch (1) { case 1: break; } }";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.TargetText == "break")).IsFalse();
    }

    [Test]
    public async Task Process_KeywordSwitch_NoHover()
    {
        var source = "void M() { switch (1) { case 1: break; } }";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.TargetText == "switch")).IsFalse();
    }

    [Test]
    public async Task Process_KeywordReturn_NoHover()
    {
        var source = "int M() { return 42; }";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.TargetText == "return")).IsFalse();
    }

    [Test]
    public async Task Process_KeywordIfElse_NoHover()
    {
        var source = "void M() { if (true) { } else { } }";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.TargetText == "if")).IsFalse();
        await Assert.That(result.Hovers.Any(h => h.TargetText == "else")).IsFalse();
    }

    [Test]
    public async Task Process_PredefinedTypeInt_HasHover()
    {
        var source = "int x = 42;";
        var result = await _processor.ProcessAsync(source);

        var intHover = result.Hovers.FirstOrDefault(h => h.TargetText == "int");
        await Assert.That(intHover).IsNotNull();
        await Assert.That(intHover!.Text).Contains("int");
    }

    [Test]
    public async Task Process_PredefinedTypeVoid_HasHover()
    {
        var source = "void M() { }";
        var result = await _processor.ProcessAsync(source);

        var voidHover = result.Hovers.FirstOrDefault(h => h.TargetText == "void");
        await Assert.That(voidHover).IsNotNull();
        await Assert.That(voidHover!.Text).Contains("void");
    }

    [Test]
    public async Task Process_PredefinedTypeString_HasHover()
    {
        var source = "string s = \"hello\";";
        var result = await _processor.ProcessAsync(source);

        var stringHover = result.Hovers.FirstOrDefault(h => h.TargetText == "string");
        await Assert.That(stringHover).IsNotNull();
        await Assert.That(stringHover!.Text).Contains("string");
    }

    [Test]
    public async Task Process_VarKeyword_HasHover()
    {
        var source = "var x = 42;";
        var result = await _processor.ProcessAsync(source);

        var varHover = result.Hovers.FirstOrDefault(h => h.TargetText == "var");
        await Assert.That(varHover).IsNotNull();
        await Assert.That(varHover!.Text).Contains("int");
    }

    // === @suppressErrors end-to-end tests ===

    [Test]
    public async Task Process_SuppressAllErrors_NoErrorsReported()
    {
        var source = "// @suppressErrors\nConsole.WriteLine(undeclared);";
        var result = await _processor.ProcessAsync(source);

        // Error-severity diagnostics are suppressed; warnings/info may still appear
        await Assert.That(result.Errors.Any(e => e.Severity == "error")).IsFalse();
        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
    }

    [Test]
    public async Task Process_SuppressAllErrors_HoversStillExtracted()
    {
        var source = "// @suppressErrors\nvar x = 42;\nConsole.WriteLine(undeclared);";
        var result = await _processor.ProcessAsync(source);

        // Hovers should still work for resolvable symbols
        await Assert.That(result.Hovers.Any(h => h.TargetText == "x")).IsTrue();
        // Only error-severity diagnostics are suppressed; warnings/info may remain
        await Assert.That(result.Errors.Any(e => e.Severity == "error")).IsFalse();
    }

    [Test]
    public async Task Process_SuppressSpecificCodes_OnlySuppressesMatching()
    {
        var source = "// @suppressErrors: CS0103\nConsole.WriteLine(undeclared);";
        var result = await _processor.ProcessAsync(source);

        // CS0103 should be suppressed
        await Assert.That(result.Errors.Any(e => e.Code == "CS0103")).IsFalse();
    }

    [Test]
    public async Task Process_SuppressSpecificCodes_OtherErrorsRemain()
    {
        // CS0246 = missing type, CS0103 = missing name — suppress only CS0246
        var source = "// @suppressErrors: CS0246\nMissingType x = undeclared;";
        var result = await _processor.ProcessAsync(source);

        // CS0246 should be suppressed, but CS0103 should remain
        await Assert.That(result.Errors.Any(e => e.Code == "CS0246")).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Code == "CS0103")).IsTrue();
    }

    [Test]
    public async Task Process_SuppressErrors_ConflictWithNoErrors_ProducesError()
    {
        var source = "// @suppressErrors\n// @noErrors\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Errors.Any(e => e.Code == "TH0003")).IsTrue();
        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
    }

    [Test]
    public async Task Process_SuppressErrors_CodeStrippedFromOutput()
    {
        var source = "// @suppressErrors\nvar x = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Code).IsEqualTo("var x = 42;");
    }

    // === Config implicit usings tests ===

    [Test]
    public async Task Process_ImplicitUsings_ReplacesDefaults()
    {
        // Use unqualified StringBuilder — only resolves if System.Text is in implicit usings.
        // Also verify System.Linq (a default) is NOT available since replace semantics removes it.
        var source = "// @noErrors\nvar sb = new StringBuilder();\nConsole.WriteLine(sb);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            ImplicitUsings = ["System", "System.Text"],
        });

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
    }

    [Test]
    public async Task Process_ImplicitUsings_ReplacesDefaults_DefaultsUnavailable()
    {
        // System.Linq is in defaults but NOT in custom implicitUsings — Enumerable should not resolve
        var source = "// @errors: CS0103\nvar items = Enumerable.Range(0, 5);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            ImplicitUsings = ["System"],
        });

        await Assert.That(result.Errors.Any(e => e.Code == "CS0103")).IsTrue();
    }

    [Test]
    public async Task Process_ImplicitUsings_EmptyArray_RemovesAllDefaults()
    {
        // With empty implicitUsings, Console (from System) should not be available without explicit using
        var source = "// @errors: CS0103\nConsole.WriteLine(42);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            ImplicitUsings = [],
        });

        await Assert.That(result.Errors.Any(e => e.Code == "CS0103")).IsTrue();
    }

    [Test]
    public async Task Process_ImplicitUsings_Null_KeepsDefaults()
    {
        // With null implicitUsings, defaults should still work
        var source = "// @noErrors\nvar list = new List<int>();\nConsole.WriteLine(list.Count);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            ImplicitUsings = null,
        });

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
    }

    // === Config langVersion/nullable tests ===

    [Test]
    public async Task Process_ConfigLangVersion_UsedWhenNoMarker()
    {
        // Top-level statements require C# 9+. Setting langVersion to 7 via config should cause compilation failure.
        var source = "var x = 42;\nConsole.WriteLine(x);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            LangVersion = "7",
        });

        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
    }

    [Test]
    public async Task Process_ConfigNullable_UsedWhenNoMarker()
    {
        // With nullable disabled at config level, assigning null to string should not warn
        var source = "string s = null;\nConsole.WriteLine(s);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            Nullable = "disable",
        });

        var nullableWarnings = result.Errors.Where(e => e.Code == "CS8600").ToList();
        await Assert.That(nullableWarnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_MarkerOverridesConfigLangVersion()
    {
        // Config says langVersion 7, but marker says latest — should compile with latest
        // Top-level statements require C# 9+, so setting config to 7 would fail, but marker overrides
        var source = "// @langVersion: latest\n// @noErrors\nvar x = (1, 2, 3);\nConsole.WriteLine(x);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            LangVersion = "7",
        });

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
    }

    [Test]
    public async Task Process_MarkerOverridesConfigNullable()
    {
        // Config says nullable disable, but marker says enable — should produce CS8600
        var source = "// @nullable: enable\n// @errors: CS8600\nstring s = null;\nConsole.WriteLine(s);";
        var result = await _processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            Nullable = "disable",
        });

        await Assert.That(result.Errors.Any(e => e.Code == "CS8600")).IsTrue();
    }
}
