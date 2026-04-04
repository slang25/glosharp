using GloSharp.Core;

namespace GloSharp.Tests;

public class RegionExtractorTests
{
    [Test]
    public async Task FindRegion_NamedRegion_ReturnsCorrectBounds()
    {
        var source = "using System;\n#region setup\nvar x = 42;\n#endregion\nvar y = 10;";
        var (start, end) = RegionExtractor.FindRegion(source, "setup");

        await Assert.That(start).IsEqualTo(1);  // #region line
        await Assert.That(end).IsEqualTo(3);     // #endregion line
    }

    [Test]
    public async Task FindRegion_NotFound_ThrowsError()
    {
        var source = "var x = 42;";

        await Assert.That(() => RegionExtractor.FindRegion(source, "nonexistent"))
            .Throws<InvalidOperationException>();
    }

    [Test]
    public async Task FindRegion_DuplicateNames_ReturnsFirstMatch()
    {
        var source = "#region setup\nvar x = 1;\n#endregion\n#region setup\nvar y = 2;\n#endregion";
        var (start, end) = RegionExtractor.FindRegion(source, "setup");

        await Assert.That(start).IsEqualTo(0);
        await Assert.That(end).IsEqualTo(2);
    }

    [Test]
    public async Task ApplyRegion_ExtractsOnlyRegionContent()
    {
        var source = "using System;\n#region getting-started\nvar x = 42;\nConsole.WriteLine(x);\n#endregion\nvar y = 10;";
        var transformed = RegionExtractor.ApplyRegion(source, "getting-started");
        var result = MarkerParser.Parse(transformed);

        await Assert.That(result.ProcessedCode).Contains("var x = 42;");
        await Assert.That(result.ProcessedCode).Contains("Console.WriteLine(x);");
        await Assert.That(result.ProcessedCode).DoesNotContain("using System;");
        await Assert.That(result.ProcessedCode).DoesNotContain("var y = 10;");
        await Assert.That(result.ProcessedCode).DoesNotContain("#region");
        await Assert.That(result.ProcessedCode).DoesNotContain("#endregion");
    }

    [Test]
    public async Task ApplyRegion_WithMarkersInside_PreservesMarkers()
    {
        var source = "using System;\n#region demo\nvar x = 42;\n//  ^?\n#endregion";
        var transformed = RegionExtractor.ApplyRegion(source, "demo");
        var result = MarkerParser.Parse(transformed);

        await Assert.That(result.HoverQueries.Count).IsEqualTo(1);
        await Assert.That(result.ProcessedCode).Contains("var x = 42;");
    }

    [Test]
    public async Task ApplyRegion_RegionDirectivesHiddenFromOutput()
    {
        var source = "#region test\nvar x = 42;\n#endregion";
        var transformed = RegionExtractor.ApplyRegion(source, "test");
        var result = MarkerParser.Parse(transformed);

        await Assert.That(result.ProcessedCode).DoesNotContain("#region");
        await Assert.That(result.ProcessedCode).DoesNotContain("#endregion");
    }

    private readonly GloSharpProcessor _processor = new();

    [Test]
    public async Task ProcessAsync_WithRegion_CompilesFullFileButOutputsRegion()
    {
        var source = "using System;\nvar helper = 42;\n#region demo\nvar result = helper * 2;\n//     ^?\n#endregion";
        var result = await _processor.ProcessAsync(source, new GloSharpProcessorOptions
        {
            RegionName = "demo",
        });

        await Assert.That(result.Code).Contains("var result = helper * 2;");
        await Assert.That(result.Code).DoesNotContain("var helper = 42;");
        var persistent = result.Hovers.First(h => h.Persistent);
        await Assert.That(persistent.Text).Contains("int");
    }
}
