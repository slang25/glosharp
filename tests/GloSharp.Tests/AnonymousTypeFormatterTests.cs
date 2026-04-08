using System.Text.Json;
using GloSharp.Core;

namespace GloSharp.Tests;

public class AnonymousTypeFormatterTests
{
    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task Process_SimpleAnonymousType_ShowsPlaceholderAndAnnotation()
    {
        var source = "var x = new { Name = \"test\", Age = 42 };\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("'a");
        await Assert.That(persistent.TypeAnnotations).IsNotNull();
        await Assert.That(persistent.TypeAnnotations!.Count).IsGreaterThanOrEqualTo(1);
        await Assert.That(persistent.TypeAnnotations![0].Name).IsEqualTo("'a");
        await Assert.That(persistent.TypeAnnotations![0].Expansion).Contains("string Name");
        await Assert.That(persistent.TypeAnnotations![0].Expansion).Contains("int Age");
    }

    [Test]
    public async Task Process_ArrayOfAnonymousTypes_ShowsPlaceholderArray()
    {
        var source = "var items = new[] { new { Id = 1 } };\n//    ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("'a");
        await Assert.That(persistent.TypeAnnotations).IsNotNull();
        await Assert.That(persistent.TypeAnnotations![0].Expansion).Contains("int Id");
    }

    [Test]
    public async Task Process_AnonymousTypePropertyAccess_ShowsAnnotation()
    {
        var source = "var x = new { Name = \"test\", Age = 42 };\nx.Name;\n// ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.TypeAnnotations).IsNotNull();
        await Assert.That(persistent.TypeAnnotations![0].Expansion).Contains("string Name");
    }

    [Test]
    public async Task Process_NestedAnonymousTypes_ShowsMultipleAnnotations()
    {
        var source = "var x = new { Name = \"test\", Details = new { Id = 1 } };\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.TypeAnnotations).IsNotNull();
        await Assert.That(persistent.TypeAnnotations!.Count).IsEqualTo(2);
    }

    [Test]
    public async Task Process_NonAnonymousType_NoTypeAnnotations()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.TypeAnnotations).IsNull();
    }

    [Test]
    public async Task Process_LinqProjectionAnonymousType_ShowsPlaceholder()
    {
        var source = "var items = new[] { 1, 2, 3 }.Select(x => new { Value = x, Doubled = x * 2 });\n//    ^?";
        var result = await _processor.ProcessAsync(source);

        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("'a");
        await Assert.That(persistent.TypeAnnotations).IsNotNull();
        await Assert.That(persistent.TypeAnnotations![0].Expansion).Contains("int Value");
        await Assert.That(persistent.TypeAnnotations![0].Expansion).Contains("int Doubled");
    }

    [Test]
    public async Task Process_AnonymousType_JsonSerializesTypeAnnotations()
    {
        var source = "var x = new { Name = \"test\" };\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var json = JsonOutput.Serialize(result);
        await Assert.That(json).Contains("\"typeAnnotations\"");
        await Assert.That(json).Contains("\"name\"");
        await Assert.That(json).Contains("\"expansion\"");
    }

    [Test]
    public async Task Process_NonAnonymousType_JsonOmitsTypeAnnotations()
    {
        var source = "var x = 42;\n//  ^?";
        var result = await _processor.ProcessAsync(source);

        var json = JsonOutput.Serialize(result);
        await Assert.That(json).DoesNotContain("\"typeAnnotations\"");
    }
}
