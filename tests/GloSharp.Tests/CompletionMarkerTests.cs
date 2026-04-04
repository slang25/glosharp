using GloSharp.Core;

namespace GloSharp.Tests;

public class CompletionMarkerTests
{
    [Test]
    public async Task Parse_CompletionMarker_ExtractsQueryPosition()
    {
        var source = "Console.\n//      ^|";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.CompletionQueries.Count).IsEqualTo(1);
        await Assert.That(result.CompletionQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.CompletionQueries[0].Column).IsEqualTo(8);
    }

    [Test]
    public async Task Parse_MultipleCompletionMarkers_ExtractsAll()
    {
        var source = "Console.\n//      ^|\nvar x = \"\";\nx.\n// ^|";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.CompletionQueries.Count).IsEqualTo(2);
        await Assert.That(result.CompletionQueries[0].OriginalLine).IsEqualTo(0);
        await Assert.That(result.CompletionQueries[1].OriginalLine).IsEqualTo(2);
    }

    [Test]
    public async Task Parse_MixedHoverAndCompletion_ExtractsBoth()
    {
        var source = "var x = 42;\n//  ^?\nConsole.\n//      ^|";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.HoverQueries.Count).IsEqualTo(1);
        await Assert.That(result.CompletionQueries.Count).IsEqualTo(1);
    }

    [Test]
    public async Task Parse_CompletionMarker_RemovedFromOutput()
    {
        var source = "Console.\n//      ^|\nvar x = 42;";
        var result = MarkerParser.Parse(source);

        await Assert.That(result.ProcessedCode).IsEqualTo("Console.\nvar x = 42;");
    }

    [Test]
    public async Task Parse_CompletionMarker_PositionRemappedAfterMarkerRemoval()
    {
        var source = "var y = 1;\n//  ^?\nConsole.\n//      ^|";
        var result = MarkerParser.Parse(source);

        // After removing ^? marker line, Console. is line 1 in processed code
        await Assert.That(result.CompletionQueries[0].OriginalLine).IsEqualTo(1);
    }

    [Test]
    public async Task GetCompilationCode_StripsCompletionMarkers()
    {
        var source = "Console.\n//      ^|\nvar x = 42;";
        var compilationCode = MarkerParser.GetCompilationCode(source);

        await Assert.That(compilationCode).DoesNotContain("^|");
        await Assert.That(compilationCode).Contains("Console.");
        await Assert.That(compilationCode).Contains("var x = 42;");
    }
}
