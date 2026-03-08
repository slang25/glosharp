using TwoHash.Core;

namespace TwoHash.Tests;

public class TwohashProcessorTests
{
    private readonly TwohashProcessor _processor = new();

    [Test]
    public async Task Process_LocalVariable_ExtractsTypeInfo()
    {
        var source = "var x = 42;\n//  ^?";
        var result = _processor.Process(source);

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
        var result = _processor.Process(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("string");
        await Assert.That(result.Hovers[0].Text).Contains("greeting");
    }

    [Test]
    public async Task Process_MethodWithOverloads_ShowsOverloadCount()
    {
        var source = "Console.WriteLine(\"test\");\n//        ^?";
        var result = _processor.Process(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("overloads");
        await Assert.That(result.Hovers[0].OverloadCount).IsNotNull();
        await Assert.That(result.Hovers[0].OverloadCount!.Value).IsGreaterThan(1);
    }

    [Test]
    public async Task Process_StructuredParts_ContainsCorrectKinds()
    {
        var source = "var x = 42;\n//  ^?";
        var result = _processor.Process(source);

        var parts = result.Hovers[0].Parts;
        await Assert.That(parts.Any(p => p.Kind == "keyword" && p.Text == "int")).IsTrue();
        await Assert.That(parts.Any(p => p.Kind == "localName" && p.Text == "x")).IsTrue();
        await Assert.That(parts.Any(p => p.Kind == "text" && p.Text == "local variable")).IsTrue();
    }

    [Test]
    public async Task Process_CleanCompilation_Succeeds()
    {
        var source = "// @noErrors\nvar x = 42;\nConsole.WriteLine(x);";
        var result = _processor.Process(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_ExpectedError_MarksAsExpected()
    {
        var source = "// @errors: CS0103\nConsole.WriteLine(undeclared);";
        var result = _processor.Process(source);

        await Assert.That(result.Errors.Any(e => e.Code == "CS0103" && e.Expected)).IsTrue();
    }

    [Test]
    public async Task Process_CutMarker_HidesSetupCode()
    {
        var source = "using System.Text;\nvar sb = new StringBuilder();\n// ---cut---\nsb.Append(\"hello\");\n//   ^?";
        var result = _processor.Process(source);

        await Assert.That(result.Code).DoesNotContain("StringBuilder()");
        await Assert.That(result.Code).Contains("sb.Append");
        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("Append");
    }

    [Test]
    public async Task Process_GenericType_ShowsFullType()
    {
        var source = "var list = new List<int> { 1, 2, 3 };\n//    ^?";
        var result = _processor.Process(source);

        await Assert.That(result.Hovers.Count).IsEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("List<int>");
    }

    [Test]
    public async Task Process_OutputFormat_HasRequiredFields()
    {
        var source = "var x = 42;";
        var result = _processor.Process(source);

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
        var result = _processor.Process(source);
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
        var result = _processor.Process(source);
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

        var result = _processor.Process(source, new TwohashProcessorOptions
        {
            ProjectPath = fixtureDir,
        });

        await Assert.That(result.Hovers.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(result.Hovers[0].Text).Contains("SerializeObject");
        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Meta.Packages.Count).IsGreaterThan(0);
        await Assert.That(result.Meta.Packages.Any(p => p.Name == "Newtonsoft.Json")).IsTrue();
    }
}
