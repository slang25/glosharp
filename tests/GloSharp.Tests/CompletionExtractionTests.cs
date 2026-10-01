using GloSharp.Core;

namespace GloSharp.Tests;

public class CompletionExtractionTests
{
    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task Process_CompletionAfterDot_ReturnsItems()
    {
        var source = "Console.\n//      ^|";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Completions.Count).IsEqualTo(1);
        await Assert.That(result.Completions[0].Items.Count).IsGreaterThan(0);
        await Assert.That(result.Completions[0].Items.Any(i => i.Label == "WriteLine")).IsTrue();
    }

    [Test]
    public async Task Process_CompletionKind_PrefersInstanceMemberOverSameNamedExtension()
    {
        // string.Trim exists as an instance method and (via first-class spans) as a
        // MemoryExtensions extension; Roslyn merges them into one item with unstable tags.
        var source = "var s = \"x\";\nvar t = s.Trim();\n//        ^|";

        for (var run = 0; run < 3; run++)
        {
            var result = await new GloSharpProcessor().ProcessAsync(source);
            var items = result.Completions[0].Items;

            await Assert.That(items.Single(i => i.Label == "Trim").Kind).IsEqualTo("Method");
            await Assert.That(items.Single(i => i.Label == "Select").Kind).IsEqualTo("ExtensionMethod");
        }
    }

    [Test]
    public async Task Process_CompletionForLocals_IncludesLocalVariable()
    {
        var source = "var myName = \"test\";\nmyN\n// ^|";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Completions.Count).IsEqualTo(1);
        await Assert.That(result.Completions[0].Items.Any(i => i.Label == "myName")).IsTrue();
    }

    [Test]
    public async Task Process_CompletionItems_HaveKindAndLabel()
    {
        var source = "Console.\n//      ^|";
        var result = await _processor.ProcessAsync(source);

        var writeLineItem = result.Completions[0].Items.FirstOrDefault(i => i.Label == "WriteLine");
        await Assert.That(writeLineItem).IsNotNull();
        await Assert.That(writeLineItem!.Kind).IsNotNull();
        await Assert.That(writeLineItem.Kind.Length).IsGreaterThan(0);
    }

    [Test]
    public async Task Process_NoCompletionMarkers_EmptyArray()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Completions.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Process_CompletionPosition_CorrectLineAndCharacter()
    {
        var source = "Console.\n//      ^|";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Completions[0].Line).IsEqualTo(0);
        await Assert.That(result.Completions[0].Character).IsEqualTo(8);
    }
}
