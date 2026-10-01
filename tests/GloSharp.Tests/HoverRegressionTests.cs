using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>
/// Caret placement, operator/punctuation hovers and LINQ range variables.
/// </summary>
public class HoverRegressionTests
{
    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task Process_CaretPastEndOfLine_IsSkippedWithWarning()
    {
        var source = "var a = 1;\n//                    ^?\nvar bbbbbbbbbbbbbbbbbbbbbbbb = \"x\";";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.Persistent)).IsFalse();
        await Assert.That(result.Meta.Warnings.Count).IsEqualTo(1);
        await Assert.That(result.Meta.Warnings[0]).StartsWith("Line 2:");
        await Assert.That(result.Meta.Warnings[0]).Contains("past the end of line 1");
        // The auto-hover for bbbb... is still on its own line
        await Assert.That(result.Hovers.Single(h => h.TargetText.StartsWith("bbbb")).Line).IsEqualTo(1);
    }

    [Test]
    public async Task Process_CaretUnderBlankLine_IsSkippedWithWarning()
    {
        var source = "Console.WriteLine(1);\n\n//  ^?\nConsole.WriteLine(2);";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.Persistent)).IsFalse();
        await Assert.That(result.Meta.Warnings.Single()).Contains("line 2, which is empty");
    }

    [Test]
    public async Task Process_CaretOnOperator_IsSkippedWithWarning()
    {
        var source = "var value = 1;\n//        ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.Persistent)).IsFalse();
        await Assert.That(result.Meta.Warnings.Single()).Contains("does not point at a symbol");
    }

    [Test]
    public async Task Process_ValidCaret_NoWarnings()
    {
        var source = "var a = 1;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Single(h => h.Persistent).TargetText).IsEqualTo("a");
        await Assert.That(result.Meta.Warnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_CompletionCaretPastEndOfLine_IsSkippedWithWarning()
    {
        var source = "Console.\n//            ^|";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Completions.Count).IsEqualTo(0);
        await Assert.That(result.Meta.Warnings.Single()).Contains("past the end of line 1");
    }

    [Test]
    public async Task Process_AutoHovers_SkipOperatorsAndPunctuation()
    {
        var source = "var c = 2 * 3;\nConsole.WriteLine(c + 1);\nint[] arr = [1, 2];\nvar first = arr[0];\nstring? s = null;\nvar len = s?.Length ?? 0;";
        var result = await _processor.ProcessAsync(source);

        string[] operators = ["*", "+", "[", "]", "?.", "??", "?", "=", "(", ")"];
        foreach (var op in operators)
            await Assert.That(result.Hovers.Any(h => h.TargetText == op)).IsFalse();

        // Identifiers still get hovers
        await Assert.That(result.Hovers.Any(h => h.TargetText == "WriteLine")).IsTrue();
        await Assert.That(result.Hovers.Any(h => h.TargetText == "Length")).IsTrue();
    }

    [Test]
    public async Task Process_RangeVariableDeclaration_ShowsRangeVariable()
    {
        var source = """
            var people = new[] { ("Ann", 31), ("Bob", 12) };
            var adults =
                from p in people
            //       ^?
                where p.Item2 > 18
                select p.Item1;
            """;
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.Single(h => h.Persistent);
        await Assert.That(persistent.TargetText).IsEqualTo("p");
        await Assert.That(persistent.Text).IsEqualTo("(range variable) (string, int) p");
        await Assert.That(persistent.SymbolKind).IsEqualTo("Local");
    }

    [Test]
    public async Task Process_RangeVariableUsage_ShowsTypedRangeVariable()
    {
        var source = """
            var people = new[] { ("Ann", 31), ("Bob", 12) };
            var adults = from p in people where p.Item2 > 18 select p.Item1;
            """;
        var result = await _processor.ProcessAsync(source);

        var usages = result.Hovers.Where(h => h.TargetText == "p").ToList();
        await Assert.That(usages.Count).IsEqualTo(3);
        foreach (var usage in usages)
            await Assert.That(usage.Text).IsEqualTo("(range variable) (string, int) p");
    }

    [Test]
    public async Task Process_UnresolvedIdentifierInInitializer_DoesNotBorrowDeclarationHover()
    {
        var source = "// @noErrors\nint total = missing;";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Any(h => h.TargetText == "missing")).IsFalse();
    }

    [Test]
    public async Task Process_AnonymousTypeMemberName_StillResolves()
    {
        var source = "var o = new { Name = \"x\" };\n//            ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.Single(h => h.Persistent);
        await Assert.That(persistent.TargetText).IsEqualTo("Name");
        await Assert.That(persistent.Text).Contains("Name");
    }

    [Test]
    public async Task Process_SingleExtraOverload_UsesSingular()
    {
        var source = """
            // ---cut-before---
            static class M
            {
                public static void F(int x) { }
                public static void F(string x) { }
            }
            M.F(1);
            //^?
            """;
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Hovers.Single(h => h.Persistent).Text).EndsWith("(+ 1 overload)");
    }
}
