using TwoHash.Core;

namespace TwoHash.Tests;

public class TwohashProcessorTests
{
    private readonly TwohashProcessor _processor = new();

    [Fact]
    public void Process_LocalVariable_ExtractsTypeInfo()
    {
        var source = "var x = 42;\n//  ^?";
        var result = _processor.Process(source);

        Assert.Single(result.Hovers);
        Assert.Contains("int", result.Hovers[0].Text);
        Assert.Contains("x", result.Hovers[0].Text);
        Assert.Equal("Local", result.Hovers[0].SymbolKind);
        Assert.Equal("x", result.Hovers[0].TargetText);
    }

    [Fact]
    public void Process_StringVariable_ShowsType()
    {
        var source = "var greeting = \"hello\";\n//      ^?";
        var result = _processor.Process(source);

        Assert.Single(result.Hovers);
        Assert.Contains("string", result.Hovers[0].Text);
        Assert.Contains("greeting", result.Hovers[0].Text);
    }

    [Fact]
    public void Process_MethodWithOverloads_ShowsOverloadCount()
    {
        var source = "Console.WriteLine(\"test\");\n//        ^?";
        var result = _processor.Process(source);

        Assert.Single(result.Hovers);
        Assert.Contains("overloads", result.Hovers[0].Text);
        Assert.NotNull(result.Hovers[0].OverloadCount);
        Assert.True(result.Hovers[0].OverloadCount > 1);
    }

    [Fact]
    public void Process_StructuredParts_ContainsCorrectKinds()
    {
        var source = "var x = 42;\n//  ^?";
        var result = _processor.Process(source);

        var parts = result.Hovers[0].Parts;
        Assert.Contains(parts, p => p.Kind == "keyword" && p.Text == "int");
        Assert.Contains(parts, p => p.Kind == "localName" && p.Text == "x");
        Assert.Contains(parts, p => p.Kind == "text" && p.Text == "local variable");
    }

    [Fact]
    public void Process_CleanCompilation_Succeeds()
    {
        var source = "// @noErrors\nvar x = 42;\nConsole.WriteLine(x);";
        var result = _processor.Process(source);

        Assert.True(result.Meta.CompileSucceeded);
        Assert.Empty(result.Errors);
    }

    [Fact]
    public void Process_ExpectedError_MarksAsExpected()
    {
        var source = "// @errors: CS0103\nConsole.WriteLine(undeclared);";
        var result = _processor.Process(source);

        Assert.Contains(result.Errors, e => e.Code == "CS0103" && e.Expected);
    }

    [Fact]
    public void Process_CutMarker_HidesSetupCode()
    {
        var source = "using System.Text;\nvar sb = new StringBuilder();\n// ---cut---\nsb.Append(\"hello\");\n//   ^?";
        var result = _processor.Process(source);

        Assert.DoesNotContain("StringBuilder()", result.Code);
        Assert.Contains("sb.Append", result.Code);
        Assert.Single(result.Hovers);
        Assert.Contains("Append", result.Hovers[0].Text);
    }

    [Fact]
    public void Process_GenericType_ShowsFullType()
    {
        var source = "var list = new List<int> { 1, 2, 3 };\n//    ^?";
        var result = _processor.Process(source);

        Assert.Single(result.Hovers);
        Assert.Contains("List<int>", result.Hovers[0].Text);
    }

    [Fact]
    public void Process_OutputFormat_HasRequiredFields()
    {
        var source = "var x = 42;";
        var result = _processor.Process(source);

        Assert.Equal("csharp", result.Lang);
        Assert.NotNull(result.Code);
        Assert.NotNull(result.Original);
        Assert.NotNull(result.Hovers);
        Assert.NotNull(result.Errors);
        Assert.NotNull(result.Completions);
        Assert.NotNull(result.Highlights);
        Assert.NotNull(result.Hidden);
        Assert.NotNull(result.Meta);
        Assert.NotNull(result.Meta.TargetFramework);
    }

    [Fact]
    public void Process_JsonSerialization_UseCamelCase()
    {
        var source = "var x = 42;\n//  ^?";
        var result = _processor.Process(source);
        var json = JsonOutput.Serialize(result);

        Assert.Contains("\"code\":", json);
        Assert.Contains("\"hovers\":", json);
        Assert.Contains("\"symbolKind\":", json);
        Assert.Contains("\"targetFramework\":", json);
        Assert.Contains("\"compileSucceeded\":", json);
        // Should not contain PascalCase
        Assert.DoesNotContain("\"Code\":", json);
        Assert.DoesNotContain("\"Hovers\":", json);
    }

    [Fact]
    public void Process_EmptyArraysNotNull()
    {
        var source = "var x = 42;";
        var result = _processor.Process(source);
        var json = JsonOutput.Serialize(result);

        Assert.Contains("\"completions\": []", json);
        Assert.Contains("\"highlights\": []", json);
        Assert.Contains("\"hidden\": []", json);
    }
}
