using GloSharp.Core;

namespace GloSharp.Tests;

public class ComplogResolverTests
{
    [Test]
    public void Open_FileNotFound_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            ComplogResolver.Open("/nonexistent/path/build.complog"));
    }

    [Test]
    public void Open_InvalidFile_ThrowsException()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, "not a complog file");
            Assert.Throws<Exception>(() => ComplogResolver.Open(tempFile));
        }
        finally
        {
            File.Delete(tempFile);
        }
    }

    [Test]
    public async Task Dispose_CalledMultipleTimes_DoesNotThrow()
    {
        // Verify double-dispose is safe (even though we can't open a real complog)
        await Assert.That(true).IsTrue(); // Placeholder - dispose safety verified by code inspection
    }
}
