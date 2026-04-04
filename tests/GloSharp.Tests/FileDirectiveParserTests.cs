using GloSharp.Core;

namespace GloSharp.Tests;

public class FileDirectiveParserTests
{
    [Test]
    public async Task Parse_PackageWithVersion_ExtractsNameAndVersion()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives.Count).IsEqualTo(1);
        await Assert.That(result.Directives[0].Type).IsEqualTo(FileDirectiveType.Package);
        await Assert.That(result.Directives[0].Name).IsEqualTo("Newtonsoft.Json");
        await Assert.That(result.Directives[0].Value).IsEqualTo("13.0.3");
    }

    [Test]
    public async Task Parse_PackageWithWildcard_ExtractsVersion()
    {
        var source = "#:package Spectre.Console@*\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives[0].Name).IsEqualTo("Spectre.Console");
        await Assert.That(result.Directives[0].Value).IsEqualTo("*");
    }

    [Test]
    public async Task Parse_PackageWithoutVersion_ExtractsNameOnly()
    {
        var source = "#:package Newtonsoft.Json\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives[0].Name).IsEqualTo("Newtonsoft.Json");
        await Assert.That(result.Directives[0].Value).IsNull();
    }

    [Test]
    public async Task Parse_SdkDirective_ExtractsSdkName()
    {
        var source = "#:sdk Microsoft.NET.Sdk.Web\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives[0].Type).IsEqualTo(FileDirectiveType.Sdk);
        await Assert.That(result.Directives[0].Name).IsEqualTo("Microsoft.NET.Sdk.Web");
    }

    [Test]
    public async Task Parse_PropertyDirective_ExtractsKeyAndValue()
    {
        var source = "#:property TargetFramework=net10.0\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives[0].Type).IsEqualTo(FileDirectiveType.Property);
        await Assert.That(result.Directives[0].Name).IsEqualTo("TargetFramework");
        await Assert.That(result.Directives[0].Value).IsEqualTo("net10.0");
    }

    [Test]
    public async Task Parse_ProjectDirective_ExtractsPath()
    {
        var source = "#:project ../SharedLib/SharedLib.csproj\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives[0].Type).IsEqualTo(FileDirectiveType.Project);
        await Assert.That(result.Directives[0].Name).IsEqualTo("../SharedLib/SharedLib.csproj");
    }

    [Test]
    public async Task Parse_MultipleDirectives_ExtractsAllInOrder()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\n#:sdk Microsoft.NET.Sdk.Web\n#:property TargetFramework=net10.0\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.Directives.Count).IsEqualTo(3);
        await Assert.That(result.Directives[0].Type).IsEqualTo(FileDirectiveType.Package);
        await Assert.That(result.Directives[1].Type).IsEqualTo(FileDirectiveType.Sdk);
        await Assert.That(result.Directives[2].Type).IsEqualTo(FileDirectiveType.Property);
    }

    [Test]
    public async Task Parse_StripsDirectivesFromOutput()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\nusing Newtonsoft.Json;\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.CleanedSource).IsEqualTo("using Newtonsoft.Json;\nvar x = 42;");
    }

    [Test]
    public async Task Parse_PreservesOriginalSource()
    {
        var source = "#:package Newtonsoft.Json@13.0.3\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.OriginalSource).IsEqualTo(source);
    }

    [Test]
    public async Task Parse_TracksRemovedLineCount()
    {
        var source = "#:package A@1.0\n#:package B@2.0\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.DirectiveLinesRemoved).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_NoDirectives_ReturnsUnchangedSource()
    {
        var source = "var x = 42;\nvar y = 10;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.CleanedSource).IsEqualTo(source);
        await Assert.That(result.Directives.Count).IsEqualTo(0);
        await Assert.That(result.DirectiveLinesRemoved).IsEqualTo(0);
        await Assert.That(result.HasDirectives).IsFalse();
    }

    [Test]
    public async Task HasDirectives_ReturnsTrueWhenPresent()
    {
        await Assert.That(FileDirectiveParser.HasDirectives("#:package Foo\nvar x = 1;")).IsTrue();
    }

    [Test]
    public async Task HasDirectives_ReturnsFalseWhenAbsent()
    {
        await Assert.That(FileDirectiveParser.HasDirectives("var x = 1;")).IsFalse();
    }

    [Test]
    public async Task GetPackageReferences_ReturnsPackages()
    {
        var source = "#:package A@1.0\n#:package B@2.0\n#:sdk Microsoft.NET.Sdk\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        var packages = result.GetPackageReferences();
        await Assert.That(packages.Count).IsEqualTo(2);
        await Assert.That(packages[0].Name).IsEqualTo("A");
        await Assert.That(packages[0].Version).IsEqualTo("1.0");
        await Assert.That(packages[1].Name).IsEqualTo("B");
        await Assert.That(packages[1].Version).IsEqualTo("2.0");
    }

    [Test]
    public async Task GetSdk_ReturnsValue()
    {
        var source = "#:sdk Microsoft.NET.Sdk.Web\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.GetSdk()).IsEqualTo("Microsoft.NET.Sdk.Web");
    }

    [Test]
    public async Task GetSdk_ReturnsNullWhenAbsent()
    {
        var source = "#:package Foo@1.0\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.GetSdk()).IsNull();
    }

    // === Line position mapping tests ===

    [Test]
    public async Task Parse_DirectivesAtTop_CodeStartsAtLineZero()
    {
        var source = "#:package A@1.0\n#:package B@2.0\nvar x = 42;\nvar y = 10;";
        var result = FileDirectiveParser.Parse(source);

        var cleanedLines = result.CleanedSource.Split('\n');
        await Assert.That(cleanedLines[0]).IsEqualTo("var x = 42;");
        await Assert.That(cleanedLines[1]).IsEqualTo("var y = 10;");
    }

    [Test]
    public async Task Parse_DirectiveLinesRemoved_MatchesCount()
    {
        var source = "#:package A@1.0\n#:sdk Microsoft.NET.Sdk\n#:property TargetFramework=net10.0\nvar x = 42;";
        var result = FileDirectiveParser.Parse(source);

        await Assert.That(result.DirectiveLinesRemoved).IsEqualTo(3);
        await Assert.That(result.CleanedSource).IsEqualTo("var x = 42;");
    }
}
