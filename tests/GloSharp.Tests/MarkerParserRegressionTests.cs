using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>
/// Cut nesting, region extraction, markers in string literals and the O(1) line maps.
/// </summary>
public class MarkerParserRegressionTests
{
    [Test]
    public async Task Parse_NestedCutStartEnd_HidesWholeOuterBlock()
    {
        var source = "var a = 1;\n// ---cut-start---\nvar b = 2;\n// ---cut-start---\nvar c = 3;\n// ---cut-end---\nvar d = 4;\n// ---cut-end---\nvar e = 5;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar e = 5;");
        await Assert.That(result.HiddenRanges.Count).IsEqualTo(1);
        await Assert.That(result.HiddenRanges[0]).IsEqualTo(new HiddenRange(1, 7));
    }

    [Test]
    public async Task Parse_StrayCutEnd_IsIgnored()
    {
        var source = "var a = 1;\n// ---cut-end---\nvar b = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 2;");
    }

    [Test]
    public async Task Region_WithCutOutside_DoesNotLeakHiddenCode()
    {
        var source = "var secret1 = 1;\n// ---cut-start---\nvar secret2 = 2;\n// ---cut-end---\nvar secret3 = 3;\n#region show\nvar shown = 4;\n#endregion";
        var result = MarkerParser.Parse(source, RegionExtractor.GetHiddenLineMask(source, "show"));

        await Assert.That(result.ProcessedCode).IsEqualTo("var shown = 4;");
        // Everything is still compiled, including the region directives
        await Assert.That(result.CompilationCode).Contains("var secret2 = 2;");
        await Assert.That(result.CompilationCode).Contains("#region show");
        await Assert.That(result.CompilationCode).Contains("#endregion");
    }

    [Test]
    public async Task Region_WithCutInside_HidesCutPart()
    {
        var source = "#region show\n// ---cut-start---\nvar setup = 1;\n// ---cut-end---\nvar shown = setup;\n#endregion";
        var result = MarkerParser.Parse(source, RegionExtractor.GetHiddenLineMask(source, "show"));

        await Assert.That(result.ProcessedCode).IsEqualTo("var shown = setup;");
    }

    [Test]
    public async Task Region_Nested_MatchesByDepth()
    {
        var source = "#region outer\nvar a = 1;\n#region inner\nvar b = 2;\n#endregion\nvar c = 3;\n#endregion\nvar after = 4;";

        var (start, end) = RegionExtractor.FindRegion(source, "outer");
        await Assert.That(start).IsEqualTo(0);
        await Assert.That(end).IsEqualTo(6);

        var outer = MarkerParser.Parse(source, RegionExtractor.GetHiddenLineMask(source, "outer"));
        await Assert.That(outer.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 2;\nvar c = 3;");

        var inner = MarkerParser.Parse(source, RegionExtractor.GetHiddenLineMask(source, "inner"));
        await Assert.That(inner.ProcessedCode).IsEqualTo("var b = 2;");
    }

    [Test]
    public async Task Region_PrefixName_DoesNotMatchLongerName()
    {
        var source = "#region demo-long\nvar a = 1;\n#endregion\n#region demo\nvar b = 2;\n#endregion";
        var result = MarkerParser.Parse(source, RegionExtractor.GetHiddenLineMask(source, "demo"));

        await Assert.That(result.ProcessedCode).IsEqualTo("var b = 2;");
    }

    [Test]
    public async Task Region_Unclosed_RunsToEndOfFile()
    {
        var source = "var a = 1;\n#region demo\nvar b = 2;\nvar c = 3;";
        var result = MarkerParser.Parse(source, RegionExtractor.GetHiddenLineMask(source, "demo"));

        await Assert.That(result.ProcessedCode).IsEqualTo("var b = 2;\nvar c = 3;");
    }

    [Test]
    public async Task Parse_MarkerInsideRawString_IsCode()
    {
        var source = "var text = \"\"\"\n    // @noErrors\n    //  ^?\n    \"\"\";\nint bad = \"x\";";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.SuppressAllErrors).IsFalse();
        await Assert.That(result.HoverQueries.Count).IsEqualTo(0);
        await Assert.That(result.ProcessedCode).IsEqualTo(source);
        await Assert.That(result.CompilationCode).IsEqualTo(source);
    }

    [Test]
    public async Task Parse_MarkerInsideVerbatimString_IsCode()
    {
        var source = "var text = @\"line one\n// ---cut---\nline three\";\nvar y = 1;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo(source);
    }

    [Test]
    public async Task Process_MarkerInsideRawString_DoesNotSuppressRealErrors()
    {
        var source = "var text = \"\"\"\n    // @noErrors\n    \"\"\";\nint bad = \"x\";";
        var result = await new GloSharpProcessor().ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
        await Assert.That(result.Code).Contains("// @noErrors");
    }

    [Test]
    public async Task Parse_LineMaps_AreConsistent()
    {
        var source = "var a = 1;\n//  ^?\n// ---cut-start---\nvar b = 2;\n// ---cut-end---\nvar c = 3;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.CompilationCode).IsEqualTo("var a = 1;\nvar b = 2;\nvar c = 3;");
        await Assert.That(result.CompilationLineMap).IsEquivalentTo(new[] { 0, 3, 5 });
        await Assert.That(result.LineMap).IsEquivalentTo(new[] { 0, 5 });
        await Assert.That(result.InputLineToProcessed).IsEquivalentTo(new[] { 0, -1, -1, -1, -1, 1 });
        await Assert.That(result.InputLineToCompilation).IsEquivalentTo(new[] { 0, -1, -1, 1, -1, 2 });
    }

    [Test]
    public async Task Parse_ErrorExpectationOnHiddenLine_IsKept()
    {
        var source = "// @errors: CS0029\nint bad = \"x\";\n// ---cut---\nvar a = 1;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ErrorExpectations.Count).IsEqualTo(1);
        await Assert.That(result.ErrorExpectations[0].OriginalLine).IsEqualTo(-1);
        await Assert.That(result.ErrorExpectations[0].InputLine).IsEqualTo(1);
    }

    [Test]
    [Arguments("CS0029, CS1503", new[] { "CS0029", "CS1503" })]
    [Arguments("CS0029 CS1503", new[] { "CS0029", "CS1503" })]
    [Arguments("CS0029,CS1503 , CS0103", new[] { "CS0029", "CS1503", "CS0103" })]
    public async Task SplitCodes_AcceptsCommasAndWhitespace(string input, string[] expected)
    {
        await Assert.That(MarkerParser.SplitCodes(input)).IsEquivalentTo(expected);
    }

    [Test]
    public async Task FileDirectiveParser_LineMap_PointsAtOriginalLines()
    {
        var result = FileDirectiveParser.Parse("#:package A@1.0\nvar x = 1;\n#:property X=Y\nvar y = 2;");

        await Assert.That(result.LineMap).IsEquivalentTo(new[] { 1, 3 });
        await Assert.That(result.DirectiveLines).IsEquivalentTo(new[] { "#:package A@1.0", "#:property X=Y" });
    }
}
