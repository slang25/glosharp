using TwoHash.Core;

namespace TwoHash.Tests;

public class MarkerParserTests
{
    [Fact]
    public void Parse_HoverMarker_ExtractsQueryPosition()
    {
        var source = "var x = 42;\n//  ^?";
        var result = MarkerParser.Parse(source);

        Assert.Single(result.HoverQueries);
        Assert.Equal(0, result.HoverQueries[0].OriginalLine); // processed line
        Assert.Equal(4, result.HoverQueries[0].Column);
    }

    [Fact]
    public void Parse_MultipleHoverMarkers_ExtractsAll()
    {
        var source = "var x = 42;\n//  ^?\nvar y = 10;\n//  ^?";
        var result = MarkerParser.Parse(source);

        Assert.Equal(2, result.HoverQueries.Count);
        Assert.Equal(0, result.HoverQueries[0].OriginalLine);
        Assert.Equal(1, result.HoverQueries[1].OriginalLine);
    }

    [Fact]
    public void Parse_ErrorsDirective_ExtractsCodes()
    {
        var source = "// @errors: CS1002\nvar x = 42";
        var result = MarkerParser.Parse(source);

        Assert.Single(result.ErrorExpectations);
        Assert.Contains("CS1002", result.ErrorExpectations[0].Codes);
    }

    [Fact]
    public void Parse_MultipleErrorCodes_ExtractsAll()
    {
        var source = "// @errors: CS1002, CS0246\nvar x = 42";
        var result = MarkerParser.Parse(source);

        Assert.Single(result.ErrorExpectations);
        Assert.Equal(2, result.ErrorExpectations[0].Codes.Count);
        Assert.Contains("CS1002", result.ErrorExpectations[0].Codes);
        Assert.Contains("CS0246", result.ErrorExpectations[0].Codes);
    }

    [Fact]
    public void Parse_NoErrors_SetsFlag()
    {
        var source = "// @noErrors\nvar x = 42;";
        var result = MarkerParser.Parse(source);
        Assert.True(result.NoErrors);
    }

    [Fact]
    public void Parse_CutMarker_HidesCodeBefore()
    {
        var source = "var setup = 1;\n// ---cut---\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        Assert.Equal("var visible = 2;", result.ProcessedCode);
    }

    [Fact]
    public void Parse_HideShow_HidesSection()
    {
        var source = "var a = 1;\n// @hide\nvar hidden = 2;\n// @show\nvar b = 3;";
        var result = MarkerParser.Parse(source);

        Assert.Equal("var a = 1;\nvar b = 3;", result.ProcessedCode);
    }

    [Fact]
    public void Parse_HideWithoutShow_HidesToEnd()
    {
        var source = "var a = 1;\n// @hide\nvar hidden = 2;";
        var result = MarkerParser.Parse(source);

        Assert.Equal("var a = 1;", result.ProcessedCode);
    }

    [Fact]
    public void Parse_RemovesAllMarkerLines()
    {
        var source = "var x = 42;\n//  ^?\n// @noErrors\nvar y = 10;";
        var result = MarkerParser.Parse(source);

        Assert.Equal("var x = 42;\nvar y = 10;", result.ProcessedCode);
    }

    [Fact]
    public void Parse_BuildsLineMap()
    {
        var source = "var x = 42;\n//  ^?\nvar y = 10;";
        var result = MarkerParser.Parse(source);

        Assert.Equal(2, result.LineMap.Length);
        Assert.Equal(0, result.LineMap[0]); // processed line 0 = original line 0
        Assert.Equal(2, result.LineMap[1]); // processed line 1 = original line 2
    }

    [Fact]
    public void GetCompilationCode_IncludesHiddenCode()
    {
        var source = "var setup = 1;\n// ---cut---\nvar visible = 2;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        Assert.Contains("var setup = 1;", compilationCode);
        Assert.Contains("var visible = 2;", compilationCode);
        Assert.DoesNotContain("---cut---", compilationCode);
    }
}
