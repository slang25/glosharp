using TwoHash.Core;

namespace TwoHash.Tests;

public class CompletionExtractionTests
{
    private readonly TwohashProcessor _processor = new();

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
