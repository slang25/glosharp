using GloSharp.Core;

namespace GloSharp.Tests;

public class ComplogSymbolParityTests
{
    [Test]
    public async Task Process_AgainstRawComplog_vs_CompactedGloContext_ProducesMatchingHovers()
    {
        var rawComplog = ComplogFixture.GetOrBuildMultiProjectComplog();

        var tempDir = Path.Combine(Path.GetTempPath(), $"glocontext-parity-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        try
        {
            var glocontext = Path.Combine(tempDir, "out.glocontext");
            ComplogCompactor.Compact(rawComplog, glocontext, new ComplogCompactionOptions());

            const string snippet = """
                using System.Collections.Generic;
                var list = new List<int> { 1, 2, 3 };
                //  ^?
                """;

            var processor = new GloSharpProcessor();

            var rawResult = await processor.ProcessAsync(snippet, new GloSharpProcessorOptions
            {
                ComplogPath = rawComplog,
            });
            var compactedResult = await processor.ProcessAsync(snippet, new GloSharpProcessorOptions
            {
                ComplogPath = glocontext,
            });

            await Assert.That(rawResult.Meta.CompileSucceeded).IsTrue();
            await Assert.That(compactedResult.Meta.CompileSucceeded).IsTrue();

            await Assert.That(compactedResult.Hovers.Count).IsEqualTo(rawResult.Hovers.Count);

            var rawPersistent = rawResult.Hovers.First(h => h.Persistent);
            var compactedPersistent = compactedResult.Hovers.First(h => h.Persistent);

            await Assert.That(compactedPersistent.Text).IsEqualTo(rawPersistent.Text);
            await Assert.That(compactedPersistent.SymbolKind).IsEqualTo(rawPersistent.SymbolKind);
            await Assert.That(compactedPersistent.TargetText).IsEqualTo(rawPersistent.TargetText);
        }
        finally { Directory.Delete(tempDir, recursive: true); }
    }
}
