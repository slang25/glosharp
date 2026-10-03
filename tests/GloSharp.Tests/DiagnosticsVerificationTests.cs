using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>
/// Errors in hidden code, unmatched <c>@errors</c> expectations and original-source positions —
/// the inputs <c>glosharp verify</c> relies on.
/// </summary>
public class DiagnosticsVerificationTests
{
    private readonly GloSharpProcessor _processor = new();

    // --- errors in hidden code ---

    [Test]
    public async Task Process_ErrorBeforeCut_FailsAndIsReportedAsHiddenError()
    {
        var source = "int setup = \"oops\";\n// ---cut---\nConsole.WriteLine(\"visible\");";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
        await Assert.That(result.Errors.Any(e => e.Code == "CS0029")).IsFalse();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(1);

        var hidden = result.HiddenErrors[0];
        await Assert.That(hidden.Code).IsEqualTo("CS0029");
        await Assert.That(hidden.Line).IsEqualTo(-1);
        await Assert.That(hidden.SourceLine).IsEqualTo(0);
        await Assert.That(hidden.SourceCharacter).IsEqualTo(12);
        await Assert.That(hidden.Expected).IsFalse();
    }

    [Test]
    public async Task Process_ErrorInsideCutStartEnd_Fails()
    {
        var source = "// ---cut-start---\nUndefinedThing();\n// ---cut-end---\nvar x = 1;\nConsole.WriteLine(x);";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
        await Assert.That(result.HiddenErrors.Any(e => e.Code == "CS0103" && e.SourceLine == 1)).IsTrue();
    }

    [Test]
    public async Task Process_WarningInHiddenCode_IsNotReported()
    {
        var source = "var unused = 1;\n// ---cut---\nConsole.WriteLine(\"visible\");";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(0);
        await Assert.That(result.Errors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_HiddenErrorSuppressedByNoErrors_Succeeds()
    {
        var source = "// @noErrors\nint setup = \"oops\";\n// ---cut---\nConsole.WriteLine(\"visible\");";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_HiddenErrorExpectedViaErrors_Succeeds()
    {
        var source = "// @errors: CS0029\nint setup = \"oops\";\n// ---cut---\nConsole.WriteLine(\"visible\");";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(1);
        await Assert.That(result.HiddenErrors[0].Expected).IsTrue();
    }

    [Test]
    public async Task Process_ErrorOutsideRegion_IsHiddenError()
    {
        var source = "int setup = \"oops\";\n#region demo\nConsole.WriteLine(\"visible\");\n#endregion";
        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions { RegionName = "demo" });

        await Assert.That(result.Code).IsEqualTo("Console.WriteLine(\"visible\");");
        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
        await Assert.That(result.HiddenErrors.Any(e => e.Code == "CS0029")).IsTrue();
    }

    [Test]
    public async Task Process_RegionDirectivesStayBalanced_NoHiddenPreprocessorErrors()
    {
        var source = "var a = 1;\n#region demo\nConsole.WriteLine(a);\n#endregion\nvar b = 2;\nConsole.WriteLine(b);";
        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions { RegionName = "demo" });

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(0);
    }

    // --- @errors expectations ---

    [Test]
    public async Task Process_ExpectedErrorNotReported_ProducesGS0003()
    {
        var source = "// @errors: CS0029\nint fine = 42;\nConsole.WriteLine(fine);";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
        var gs = result.Errors.Single(e => e.Code == "GS0003");
        await Assert.That(gs.Line).IsEqualTo(0);
        await Assert.That(gs.SourceLine).IsEqualTo(1);
        await Assert.That(gs.Severity).IsEqualTo("error");
        await Assert.That(gs.Message).Contains("CS0029");
    }

    [Test]
    public async Task Process_ExpectedErrorsSpaceSeparated_BothMatch()
    {
        var source = "// @errors: CS0029 CS0103\nint n = \"three\"; Undefined();";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors.Where(e => e.Severity == "error").All(e => e.Expected)).IsTrue();
        await Assert.That(result.Errors.Any(e => e.Code == "GS0003")).IsFalse();
    }

    [Test]
    public async Task Process_ExpectedErrorsCommaSeparated_BothMatch()
    {
        var source = "// @errors: CS0029,CS0103\nint n = \"three\"; Undefined();";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
    }

    [Test]
    public async Task Process_ExpectedErrorsPartiallyMatched_ReportsOnlyTheMissingCode()
    {
        var source = "// @errors: CS0029, CS1503\nint n = \"three\";";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsFalse();
        var gs = result.Errors.Where(e => e.Code == "GS0003").ToList();
        await Assert.That(gs.Count).IsEqualTo(1);
        await Assert.That(gs[0].Message).Contains("CS1503");
        await Assert.That(result.Errors.Single(e => e.Code == "CS0029").Expected).IsTrue();
    }

    [Test]
    public async Task Process_UnmatchedExpectation_SuppressedByNoErrors()
    {
        var source = "// @noErrors\n// @errors: CS0029\nint fine = 42;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Meta.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors.Count).IsEqualTo(0);
    }

    // --- source positions ---

    [Test]
    public async Task Process_Errors_CarryOriginalSourcePosition()
    {
        var source = "var a = 1;\n//  ^?\n// @errors: CS0029\nint b = \"x\";\nint c = \"y\";";
        var result = await _processor.ProcessAsync(source);

        var unexpected = result.Errors.Single(e => e.Code == "CS0029" && !e.Expected);
        await Assert.That(unexpected.Line).IsEqualTo(2);
        await Assert.That(unexpected.SourceLine).IsEqualTo(4);
        await Assert.That(unexpected.Character).IsEqualTo(8);
        await Assert.That(unexpected.SourceCharacter).IsEqualTo(8);

        var expected = result.Errors.Single(e => e.Code == "CS0029" && e.Expected);
        await Assert.That(expected.SourceLine).IsEqualTo(3);
    }

    [Test]
    public async Task Process_ErrorsInRegion_CarryOriginalSourceLine()
    {
        var source = "var helper = 1;\n#region demo\nConsole.WriteLine(helper);\nint bad = \"x\";\n#endregion";
        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions { RegionName = "demo" });

        var error = result.Errors.Single(e => e.Code == "CS0029");
        await Assert.That(error.Line).IsEqualTo(1);
        await Assert.That(error.SourceLine).IsEqualTo(3);
    }

    [Test]
    public async Task Process_InvalidLangVersionMarker_ReportsDirectiveLine()
    {
        var source = "var x = 1;\n// @langVersion: 99\nvar y = 2;";
        var result = await _processor.ProcessAsync(source);

        var error = result.Errors.Single();
        await Assert.That(error.Code).IsEqualTo("GS0001");
        await Assert.That(error.SourceLine).IsEqualTo(1);
    }
}
