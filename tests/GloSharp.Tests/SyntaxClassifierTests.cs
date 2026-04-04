using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using GloSharp.Core;

namespace GloSharp.Tests;

public class SyntaxClassifierTests
{
    private static async Task<List<ClassifiedToken>> ClassifySource(string source)
    {
        var tree = CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(LanguageVersion.Latest));
        var references = FrameworkResolver.GetFrameworkReferences("net8.0");
        var globalUsings = "global using System;\nglobal using System.Collections.Generic;\nglobal using System.Linq;";
        var globalUsingsTree = CSharpSyntaxTree.ParseText(globalUsings, path: "__GlobalUsings.cs");

        var compilation = CSharpCompilation.Create(
            "Test",
            [tree, globalUsingsTree],
            references,
            new CSharpCompilationOptions(OutputKind.ConsoleApplication)
                .WithNullableContextOptions(NullableContextOptions.Enable));

        return await SyntaxClassifier.ClassifyAsync(source, compilation, tree);
    }

    [Test]
    public async Task Classify_Keywords_ReturnsKeywordKind()
    {
        var tokens = await ClassifySource("var x = 42;");
        var varToken = tokens.FirstOrDefault(t => t.Text == "var");

        await Assert.That(varToken).IsNotNull();
        await Assert.That(varToken!.Kind).IsEqualTo("keyword");
    }

    [Test]
    public async Task Classify_StringLiterals_ReturnsStringKind()
    {
        var tokens = await ClassifySource("var s = \"hello\";");
        var stringToken = tokens.FirstOrDefault(t => t.Text == "\"hello\"");

        await Assert.That(stringToken).IsNotNull();
        await Assert.That(stringToken!.Kind).IsEqualTo("string");
    }

    [Test]
    public async Task Classify_ClassName_ReturnsClassNameKind()
    {
        var tokens = await ClassifySource("Console.WriteLine(\"hi\");");
        var consoleToken = tokens.FirstOrDefault(t => t.Text == "Console");

        await Assert.That(consoleToken).IsNotNull();
        await Assert.That(consoleToken!.Kind).IsEqualTo("className");
    }

    [Test]
    public async Task Classify_Comments_ReturnsCommentKind()
    {
        var tokens = await ClassifySource("// this is a comment\nvar x = 1;");
        var commentToken = tokens.FirstOrDefault(t => t.Text.Contains("this is a comment"));

        await Assert.That(commentToken).IsNotNull();
        await Assert.That(commentToken!.Kind).IsEqualTo("comment");
    }

    [Test]
    public async Task Classify_NumericLiterals_ReturnsNumberKind()
    {
        var tokens = await ClassifySource("var x = 42;");
        var numToken = tokens.FirstOrDefault(t => t.Text == "42");

        await Assert.That(numToken).IsNotNull();
        await Assert.That(numToken!.Kind).IsEqualTo("number");
    }

    [Test]
    public async Task Classify_MethodName_ReturnsMethodNameKind()
    {
        var tokens = await ClassifySource("Console.WriteLine(\"hi\");");
        var methodToken = tokens.FirstOrDefault(t => t.Text == "WriteLine");

        await Assert.That(methodToken).IsNotNull();
        await Assert.That(methodToken!.Kind).IsEqualTo("methodName");
    }

    [Test]
    public async Task Classify_NoDuplicateTokens()
    {
        var tokens = await ClassifySource("Console.WriteLine(\"hi\");");
        var consoleTokens = tokens.Where(t => t.Text == "Console").ToList();

        await Assert.That(consoleTokens.Count).IsEqualTo(1);
    }
}

public class ClassificationMappingTests
{
    [Test]
    public async Task MapClassificationType_KnownType_MapsCorrectly()
    {
        await Assert.That(SyntaxClassifier.MapClassificationType("class name")).IsEqualTo("className");
    }

    [Test]
    public async Task MapClassificationType_VerbatimString_MapsToString()
    {
        await Assert.That(SyntaxClassifier.MapClassificationType("string - verbatim")).IsEqualTo("string");
    }

    [Test]
    public async Task MapClassificationType_UnknownType_FallsBackToText()
    {
        await Assert.That(SyntaxClassifier.MapClassificationType("some-unknown-type")).IsEqualTo("text");
    }

    [Test]
    public async Task MapClassificationType_Keyword_MapsToKeyword()
    {
        await Assert.That(SyntaxClassifier.MapClassificationType("keyword")).IsEqualTo("keyword");
    }

    [Test]
    public async Task MapClassificationType_ControlKeyword_MapsToKeyword()
    {
        await Assert.That(SyntaxClassifier.MapClassificationType("keyword - control")).IsEqualTo("keyword");
    }
}
