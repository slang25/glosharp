using GloSharp.Core;

namespace GloSharp.Tests;

public class CompletionFilteringTests
{
    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task Process_Completion_FiltersByTypedPrefix()
    {
        var source = "var sb = new System.Text.StringBuilder();\nsb.App\n//    ^|";
        var result = await _processor.ProcessAsync(source);

        var items = result.Completions.Single().Items;
        await Assert.That(items.Count).IsGreaterThan(0);
        await Assert.That(items.All(i => i.Label.StartsWith("App", StringComparison.OrdinalIgnoreCase))).IsTrue();
        await Assert.That(items.Any(i => i.Label == "Append")).IsTrue();
        await Assert.That(items.Any(i => i.Label == "AppendLine")).IsTrue();
    }

    [Test]
    public async Task Process_Completion_HasNoDuplicateLabels()
    {
        var source = "var list = new List<int>();\nlist.\n//   ^|";
        var result = await _processor.ProcessAsync(source);

        var labels = result.Completions.Single().Items.Select(i => i.Label).ToList();
        await Assert.That(labels.Count).IsGreaterThan(0);
        await Assert.That(labels.Distinct().Count()).IsEqualTo(labels.Count);
    }

    [Test]
    public async Task Process_Completion_EmptyListWarns()
    {
        var source = "var x = 1;\nx.Zzzzzz\n//      ^|";
        var result = await _processor.ProcessAsync(source);

        await Assert.That(result.Completions.Single().Items.Count).IsEqualTo(0);
        await Assert.That(result.Meta.Warnings.Single()).Contains("produced no completions");
    }
}
