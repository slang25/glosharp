using GloSharp.Core;

namespace GloSharp.Tests;

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
    public async Task Parse_NoErrors_SetsSuppressAllErrors()
    {
        var source = "// @noErrors\nvar x = 42;";
        var result = MarkerParser.Parse(source);
        await Assert.That(result.SuppressAllErrors).IsTrue();
    }

    [Test]
    public async Task Parse_CutMarker_HidesCodeBefore()
    {
        var source = "var setup = 1;\n// ---cut---\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 2;");
    }

    [Test]
    public async Task Parse_CutStartEnd_HidesSection()
    {
        var source = "var a = 1;\n// ---cut-start---\nvar hidden = 2;\n// ---cut-end---\nvar b = 3;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 3;");
    }

    [Test]
    public async Task Parse_CutStartWithoutEnd_HidesToEnd()
    {
        var source = "var a = 1;\n// ---cut-start---\nvar hidden = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;");
    }

    [Test]
    public async Task Parse_CutAfter_HidesCodeAfter()
    {
        var source = "var visible = 1;\n// ---cut-after---\nvar hidden = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 1;");
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

    // === Highlight directive tests ===

    [Test]
    public async Task Parse_HighlightBare_TargetsNextLine()
    {
        var source = "// @highlight\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("highlight");
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0); // processed line 0
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_HighlightSingleLine_TargetsSpecifiedLine()
    {
        var source = "var a = 1;\nvar b = 2;\nvar c = 3;\n// @highlight: 2";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("highlight");
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(1); // processed line 1 (0-based), which is line 2 (1-based)
    }

    [Test]
    public async Task Parse_HighlightRange_ExpandsToPerLineEntries()
    {
        var source = "var a = 1;\nvar b = 2;\nvar c = 3;\n// @highlight: 1-3";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(3);
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0);
        await Assert.That(result.Highlights[1].TargetOriginalLine).IsEqualTo(1);
        await Assert.That(result.Highlights[2].TargetOriginalLine).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_HighlightStripped_FromOutput()
    {
        var source = "var a = 1;\n// @highlight\nvar b = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 2;");
    }

    // === Focus directive tests ===

    [Test]
    public async Task Parse_FocusBare_TargetsNextLine()
    {
        var source = "// @focus\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("focus");
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_FocusRange_ExpandsToPerLineEntries()
    {
        var source = "var a = 1;\nvar b = 2;\nvar c = 3;\n// @focus: 2-3";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(2);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("focus");
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(1);
        await Assert.That(result.Highlights[1].TargetOriginalLine).IsEqualTo(2);
    }

    // === Diff directive tests ===

    [Test]
    public async Task Parse_DiffAdd_TargetsNextLine()
    {
        var source = "// @diff: +\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("add");
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_DiffRemove_TargetsNextLine()
    {
        var source = "// @diff: -\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].Kind).IsEqualTo("remove");
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_DiffStripped_FromOutput()
    {
        var source = "var a = 1;\n// @diff: +\nvar b = 2;\n// @diff: -\nvar c = 3;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 2;\nvar c = 3;");
    }

    // === Coexistence tests ===

    [Test]
    public async Task Parse_HighlightWithHover_BothWork()
    {
        var source = "// @highlight\nvar x = 42;\n//  ^?";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.HoverQueries.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0);
        await Assert.That(result.HoverQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_DirectiveAfterCut_CorrectPositions()
    {
        var source = "var setup = 1;\n// ---cut---\n// @highlight\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 2;");
        await Assert.That(result.Highlights.Count).IsEqualTo(1);
        await Assert.That(result.Highlights[0].TargetOriginalLine).IsEqualTo(0); // only visible line
    }

    [Test]
    public async Task GetCompilationCode_ExcludesDirectiveMarkers()
    {
        var source = "var a = 1;\n// @highlight\nvar b = 2;\n// @focus: 1\n// @diff: +\nvar c = 3;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).IsEqualTo("var a = 1;\nvar b = 2;\nvar c = 3;");
    }

    // === LangVersion and Nullable marker tests ===

    [Test]
    public async Task Parse_LangVersion_ExtractsValue()
    {
        var source = "// @langVersion: 12\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.LangVersion).IsEqualTo("12");
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_LangVersion_CaseInsensitive()
    {
        var source = "// @langVersion: Latest\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.LangVersion).IsEqualTo("latest");
    }

    [Test]
    public async Task Parse_LangVersion_LastOneWins()
    {
        var source = "// @langVersion: 12\n// @langVersion: 11\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.LangVersion).IsEqualTo("11");
    }

    [Test]
    public async Task Parse_Nullable_ExtractsValue()
    {
        var source = "// @nullable: disable\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Nullable).IsEqualTo("disable");
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_Nullable_CaseInsensitive()
    {
        var source = "// @nullable: Disable\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.Nullable).IsEqualTo("disable");
    }

    [Test]
    public async Task Parse_LangVersionAndNullable_BothStrippedFromOutput()
    {
        var source = "// @langVersion: 12\n// @nullable: disable\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.LangVersion).IsEqualTo("12");
        await Assert.That(result.Nullable).IsEqualTo("disable");
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_LangVersion_PositionOffsetCorrect()
    {
        var source = "// @langVersion: 12\n// @nullable: disable\nvar x = 42;\n//  ^?";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.HoverQueries.Count).IsEqualTo(1);
        await Assert.That(result.HoverQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_NoLangVersionOrNullable_ReturnsNull()
    {
        var source = "var x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.LangVersion).IsNull();
        await Assert.That(result.Nullable).IsNull();
    }

    [Test]
    public async Task GetCompilationCode_ExcludesLangVersionAndNullable()
    {
        var source = "// @langVersion: 12\n// @nullable: enable\nvar x = 42;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).IsEqualTo("var x = 42;");
    }

    // === @suppressErrors directive tests ===

    [Test]
    public async Task Parse_SuppressErrors_SetsAllFlag()
    {
        var source = "// @suppressErrors\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.SuppressAllErrors).IsTrue();
        await Assert.That(result.SuppressedErrorCodes.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Parse_SuppressErrors_WithCodes_ExtractsCodes()
    {
        var source = "// @suppressErrors: CS0246, CS0103\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.SuppressAllErrors).IsFalse();
        await Assert.That(result.SuppressedErrorCodes.Count).IsEqualTo(2);
        await Assert.That(result.SuppressedErrorCodes).Contains("CS0246");
        await Assert.That(result.SuppressedErrorCodes).Contains("CS0103");
    }

    [Test]
    public async Task Parse_SuppressErrors_WithSingleCode()
    {
        var source = "// @suppressErrors: CS0246\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.SuppressedErrorCodes.Count).IsEqualTo(1);
        await Assert.That(result.SuppressedErrorCodes).Contains("CS0246");
    }

    [Test]
    public async Task Parse_SuppressErrors_StrippedFromOutput()
    {
        var source = "// @suppressErrors: CS0246\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_SuppressErrors_PositionOffsetCorrect()
    {
        var source = "// @suppressErrors\nvar x = 42;\n//  ^?";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.HoverQueries.Count).IsEqualTo(1);
        await Assert.That(result.HoverQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.ProcessedCode).IsEqualTo("var x = 42;");
    }

    [Test]
    public async Task Parse_SuppressErrors_CoexistsWithPerLineErrors()
    {
        var source = "// @suppressErrors: CS0246\n// @errors: CS1002\nvar x = 42";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.SuppressedErrorCodes).Contains("CS0246");
        await Assert.That(result.ErrorExpectations.Count).IsEqualTo(1);
        await Assert.That(result.ErrorExpectations[0].Codes).Contains("CS1002");
    }

    [Test]
    public async Task GetCompilationCode_ExcludesSuppressErrors()
    {
        var source = "// @suppressErrors: CS0246\nvar x = 42;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).IsEqualTo("var x = 42;");
    }

    // === ---cut-before--- directive tests ===

    [Test]
    public async Task Parse_CutBefore_HidesCodeBefore()
    {
        var source = "var setup = 1;\n// ---cut-before---\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 2;");
    }

    [Test]
    public async Task Parse_CutBefore_Indented_StillRecognized()
    {
        var source = "var setup = 1;\n  // ---cut-before---\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 2;");
    }

    [Test]
    public async Task GetCompilationCode_CutBefore_IncludesHiddenCode()
    {
        var source = "var setup = 1;\n// ---cut-before---\nvar visible = 2;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).Contains("var setup = 1;");
        await Assert.That(compilationCode).Contains("var visible = 2;");
        await Assert.That(compilationCode).DoesNotContain("---cut-before---");
    }

    [Test]
    public async Task Parse_CutMarkerStillWorks_ShortForm()
    {
        // Verify ---cut--- is still recognized as shorthand for ---cut-before---
        var source = "var setup = 1;\n// ---cut---\nvar visible = 2;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var visible = 2;");
    }

    [Test]
    public async Task Parse_FirstCutBeforeWins_WhenMultiplePresent()
    {
        var source = "var a = 1;\n// ---cut-before---\nvar b = 2;\n// ---cut---\nvar c = 3;";
        var result = MarkerParser.Parse(source);

        // The first cut-before marker wins: it hides everything above it.
        // The later ---cut--- is still stripped as a marker line but doesn't move the cut point.
        await Assert.That(result.ProcessedCode).IsEqualTo("var b = 2;\nvar c = 3;");
    }

    // === ---cut-after--- directive tests ===

    [Test]
    public async Task GetCompilationCode_CutAfter_IncludesHiddenCode()
    {
        var source = "var visible = 1;\n// ---cut-after---\nvar hidden = 2;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).Contains("var visible = 1;");
        await Assert.That(compilationCode).Contains("var hidden = 2;");
        await Assert.That(compilationCode).DoesNotContain("---cut-after---");
    }

    [Test]
    public async Task Parse_CutBeforeAndCutAfter_ShowsMiddle()
    {
        var source = "var a = 1;\n// ---cut-before---\nvar b = 2;\n// ---cut-after---\nvar c = 3;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var b = 2;");
    }

    // === Multiple ---cut-start---/---cut-end--- pairs ===

    [Test]
    public async Task Parse_MultipleCutStartEnd_HidesAllRanges()
    {
        var source = "var a = 1;\n// ---cut-start---\nvar hidden1 = 2;\n// ---cut-end---\nvar b = 3;\n// ---cut-start---\nvar hidden2 = 4;\n// ---cut-end---\nvar c = 5;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("var a = 1;\nvar b = 3;\nvar c = 5;");
    }
}
