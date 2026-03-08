using TwoHash.Core;

namespace TwoHash.Tests;

public class MarkerParserTests
{
    [Test]
    public async Task Parse_HoverMarker_ExtractsQueryPosition()
    {
        var source = "var x = 42;\n//  ^?";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.HoverQueries.Count).IsEqualTo(1);
        await Assert.That(result.HoverQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.HoverQueries[0].Column).IsEqualTo(4);
    }

    [Test]
    public async Task Parse_MultipleHoverMarkers_ExtractsAll()
    {
        var source = "var x = 42;\n//  ^?\nvar y = 10;\n//  ^?";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.HoverQueries.Count).IsEqualTo(2);
        await Assert.That(result.HoverQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.HoverQueries[1].OriginalLine).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_ErrorsDirective_ExtractsCodes()
    {
        var source = "// @errors: CS1002\nvar x = 42";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ErrorExpectations.Count).IsEqualTo(1);
        await Assert.That(result.ErrorExpectations[0].Codes).Contains("CS1002");
    }

    [Test]
    public async Task Parse_MultipleErrorCodes_ExtractsAll()
    {
        var source = "// @errors: CS1002, CS0246\nvar x = 42";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ErrorExpectations.Count).IsEqualTo(1);
        await Assert.That(result.ErrorExpectations[0].Codes.Count).IsEqualTo(2);
        await Assert.That(result.ErrorExpectations[0].Codes).Contains("CS1002");
        await Assert.That(result.ErrorExpectations[0].Codes).Contains("CS0246");
    }

    [Test]
    public async Task Parse_NoErrors_SetsFlag()
    {
        var source = "// @noErrors\nvar x = 42;";
        var result = MarkerParser.Parse(source);
        await Assert.That(result.NoErrors).IsTrue();
    }

    [Test]
    public async Task Parse_CutMarker_HidesCodeBefore()
    {
        var source = "var setup = 1;\n// ---cut---\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 2;");
    }

    [Test]
    public async Task Parse_HideShow_HidesSection()
    {
        var source = "var a = 1;\n// @hide\nvar hidden = 2;\n// @show\nvar b = 3;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 3;");
    }

    [Test]
    public async Task Parse_HideWithoutShow_HidesToEnd()
    {
        var source = "var a = 1;\n// @hide\nvar hidden = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;");
    }

    [Test]
    public async Task Parse_RemovesAllMarkerLines()
    {
        var source = "var x = 42;\n//  ^?\n// @noErrors\nvar y = 10;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;\nvar y = 10;");
    }

    [Test]
    public async Task Parse_BuildsLineMap()
    {
        var source = "var x = 42;\n//  ^?\nvar y = 10;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.LineMap.Length).IsEqualTo(2);
        await Assert.That(result.LineMap[0]).IsEqualTo(0);
        await Assert.That(result.LineMap[1]).IsEqualTo(2);
    }

    [Test]
    public async Task GetCompilationCode_IncludesHiddenCode()
    {
        var source = "var setup = 1;\n// ---cut---\nvar visible = 2;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).Contains("var setup = 1;");
        await Assert.That(compilationCode).Contains("var visible = 2;");
        await Assert.That(compilationCode).DoesNotContain("---cut---");
    }
}
