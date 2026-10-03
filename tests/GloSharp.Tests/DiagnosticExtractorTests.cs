using GloSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Text;

namespace GloSharp.Tests;

/// <summary>
/// <see cref="DiagnosticExtractor.Classify"/> on synthetic diagnostics: expected / unexpected,
/// suppressed, hidden code and GS0003, without compiling anything.
/// </summary>
public class DiagnosticExtractorTests
{
    private static (Snippet Snippet, SyntaxTree Tree) Prepare(string source)
    {
        var snippet = Snippet.Prepare(source, regionName: null);
        return (snippet, CSharpSyntaxTree.ParseText(snippet.Markers.CompilationCode));
    }

    private static Diagnostic Diag(string id, DiagnosticSeverity severity, SyntaxTree tree, string needle, int occurrence = 0)
    {
        var text = tree.GetText().ToString();
        var start = -1;
        for (var i = 0; i <= occurrence; i++)
            start = text.IndexOf(needle, start + 1, StringComparison.Ordinal);
        if (start < 0) throw new ArgumentException($"'{needle}' not found");

        var descriptor = new DiagnosticDescriptor(id, id, $"message {id}", "test", severity, isEnabledByDefault: true);
        return Diagnostic.Create(descriptor, Location.Create(tree, new TextSpan(start, needle.Length)));
    }

    [Test]
    public async Task ExpectedError_IsMarkedExpected_UnexpectedOneFails()
    {
        var (snippet, tree) = Prepare("// @errors: CS0029\nint a = \"x\";\nint b = \"y\";");

        var result = DiagnosticExtractor.Classify(snippet, tree,
        [
            Diag("CS0029", DiagnosticSeverity.Error, tree, "\"y\""),
            Diag("CS0029", DiagnosticSeverity.Error, tree, "\"x\""),
        ]);

        await Assert.That(result.CompileSucceeded).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(2);

        // Ordered by position, not input order
        await Assert.That(result.Errors[0].Line).IsEqualTo(0);
        await Assert.That(result.Errors[0].Character).IsEqualTo(8);
        await Assert.That(result.Errors[0].Expected).IsTrue();
        await Assert.That(result.Errors[0].SourceLine).IsEqualTo(1);
        await Assert.That(result.Errors[0].Message).IsEqualTo("message CS0029");

        await Assert.That(result.Errors[1].Line).IsEqualTo(1);
        await Assert.That(result.Errors[1].Expected).IsFalse();
    }

    [Test]
    public async Task OnlyExpectedErrors_Succeeds()
    {
        var (snippet, tree) = Prepare("// @errors: CS0029, CS1002\nint a = \"x\"");

        var result = DiagnosticExtractor.Classify(snippet, tree,
        [
            Diag("CS0029", DiagnosticSeverity.Error, tree, "\"x\""),
            Diag("CS1002", DiagnosticSeverity.Error, tree, "a"),
        ]);

        await Assert.That(result.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors.All(e => e.Expected)).IsTrue();
    }

    [Test]
    public async Task SuppressedDiagnostic_IsHidden_ButStillSatisfiesItsExpectation()
    {
        var (snippet, tree) = Prepare("// @suppressErrors: CS0029\n// @errors: CS0029\nint a = \"x\";");

        var result = DiagnosticExtractor.Classify(snippet, tree,
            [Diag("CS0029", DiagnosticSeverity.Error, tree, "\"x\"")]);

        await Assert.That(result.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.HiddenErrors).IsEmpty();
    }

    [Test]
    public async Task NoErrors_SuppressesEverySeverity()
    {
        var (snippet, tree) = Prepare("// @noErrors\nint a = \"x\";");

        var result = DiagnosticExtractor.Classify(snippet, tree,
        [
            Diag("CS0029", DiagnosticSeverity.Error, tree, "\"x\""),
            Diag("CS0219", DiagnosticSeverity.Warning, tree, "a"),
        ]);

        await Assert.That(result.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task HiddenCode_ErrorsGoToHiddenErrors_WarningsAreDropped()
    {
        var (snippet, tree) = Prepare("int setup = \"oops\";\n// ---cut---\nConsole.WriteLine();");

        var result = DiagnosticExtractor.Classify(snippet, tree,
        [
            Diag("CS0029", DiagnosticSeverity.Error, tree, "\"oops\""),
            Diag("CS0219", DiagnosticSeverity.Warning, tree, "setup"),
        ]);

        await Assert.That(result.CompileSucceeded).IsFalse();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(1);
        await Assert.That(result.HiddenErrors[0].Line).IsEqualTo(-1);
        await Assert.That(result.HiddenErrors[0].SourceLine).IsEqualTo(0);
        await Assert.That(result.HiddenErrors[0].SourceCharacter).IsEqualTo(12);
    }

    [Test]
    public async Task UnmatchedExpectation_ReportsGS0003OnTheTargetLine()
    {
        var (snippet, tree) = Prepare("// @errors: CS0029\n  var ok = 1;");

        var result = DiagnosticExtractor.Classify(snippet, tree, []);

        await Assert.That(result.CompileSucceeded).IsFalse();
        await Assert.That(result.Errors.Count).IsEqualTo(1);
        var error = result.Errors[0];
        await Assert.That(error.Code).IsEqualTo(GloSharpDiagnosticCodes.UnmatchedExpectedError);
        await Assert.That(error.Line).IsEqualTo(0);
        await Assert.That(error.Character).IsEqualTo(2);
        await Assert.That(error.Length).IsEqualTo("var ok = 1;".Length);
        await Assert.That(error.SourceLine).IsEqualTo(1);
    }

    [Test]
    public async Task UnmatchedExpectation_ForASuppressedCode_IsNotReported()
    {
        var (snippet, tree) = Prepare("// @suppressErrors: CS0029\n// @errors: CS0029\nvar ok = 1;");

        var result = DiagnosticExtractor.Classify(snippet, tree, []);

        await Assert.That(result.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
    }

    [Test]
    public async Task UnmatchedExpectation_InHiddenCode_GoesToHiddenErrors()
    {
        var (snippet, tree) = Prepare("// @errors: CS0029\nvar setup = 1;\n// ---cut---\nConsole.WriteLine(setup);");

        var result = DiagnosticExtractor.Classify(snippet, tree, []);

        await Assert.That(result.CompileSucceeded).IsFalse();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.HiddenErrors.Count).IsEqualTo(1);
        await Assert.That(result.HiddenErrors[0].Code).IsEqualTo(GloSharpDiagnosticCodes.UnmatchedExpectedError);
        await Assert.That(result.HiddenErrors[0].Line).IsEqualTo(-1);
    }

    [Test]
    public async Task OtherTreesAndHiddenSeverity_AreIgnored()
    {
        var (snippet, tree) = Prepare("var x = 1;");
        var otherTree = CSharpSyntaxTree.ParseText("global using System;");

        var result = DiagnosticExtractor.Classify(snippet, tree,
        [
            Diag("CS9999", DiagnosticSeverity.Error, otherTree, "System"),
            Diag("IDE0001", DiagnosticSeverity.Hidden, tree, "x"),
        ]);

        await Assert.That(result.CompileSucceeded).IsTrue();
        await Assert.That(result.Errors).IsEmpty();
        await Assert.That(result.HiddenErrors).IsEmpty();
    }

    [Test]
    public async Task MultiLineSpan_ReportsEndPosition()
    {
        var (snippet, tree) = Prepare("Foo(\n  1);");

        var result = DiagnosticExtractor.Classify(snippet, tree,
            [Diag("CS0103", DiagnosticSeverity.Error, tree, "Foo(\n  1")]);

        var error = result.Errors.Single();
        await Assert.That(error.Line).IsEqualTo(0);
        await Assert.That(error.EndLine).IsEqualTo(1);
        await Assert.That(error.EndCharacter).IsEqualTo(3);
        await Assert.That(error.Severity).IsEqualTo("error");
    }

    [Test]
    public async Task DirectiveLines_ShiftSourcePositions()
    {
        var (snippet, tree) = Prepare("#:property LangVersion=preview\nint a = \"x\";");

        var result = DiagnosticExtractor.Classify(snippet, tree,
            [Diag("CS0029", DiagnosticSeverity.Error, tree, "\"x\"")]);

        await Assert.That(result.Errors[0].Line).IsEqualTo(0);
        await Assert.That(result.Errors[0].SourceLine).IsEqualTo(1);
    }
}
