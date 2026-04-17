using GloSharp.Core;

namespace GloSharp.Tests;

public class CompilationContextResolverFactoryTests
{
    [Test]
    public void Open_MissingFile_ThrowsFileNotFoundException()
    {
        Assert.Throws<FileNotFoundException>(() =>
            CompilationContextResolverFactory.Open("/nonexistent/path/context.glocontext"));
    }

    [Test]
    public void Open_TooSmall_ThrowsInvalidData()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(tempFile, new byte[] { 0x01, 0x02 });
            Assert.Throws<InvalidDataException>(() =>
                CompilationContextResolverFactory.Open(tempFile));
        }
        finally { File.Delete(tempFile); }
    }

    [Test]
    public void Open_UnknownMagic_ThrowsInvalidData()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            var bytes = new byte[64];
            bytes[0] = (byte)'N'; bytes[1] = (byte)'O'; bytes[2] = (byte)'P'; bytes[3] = (byte)'E';
            File.WriteAllBytes(tempFile, bytes);
            Assert.Throws<InvalidDataException>(() =>
                CompilationContextResolverFactory.Open(tempFile));
        }
        finally { File.Delete(tempFile); }
    }

    [Test]
    public async Task Open_ZipMagic_RoutesToComplogResolver()
    {
        // Minimal zip header (PK\x03\x04) — ComplogResolver will fail to parse it as a real
        // complog but the factory should route to it based on magic, not to GloContextResolver.
        var tempFile = Path.GetTempFileName();
        try
        {
            var bytes = new byte[64];
            bytes[0] = 0x50; bytes[1] = 0x4B; bytes[2] = 0x03; bytes[3] = 0x04;
            File.WriteAllBytes(tempFile, bytes);

            Exception? caught = null;
            try { CompilationContextResolverFactory.Open(tempFile); }
            catch (Exception e) { caught = e; }
            await Assert.That(caught).IsNotNull();
            await Assert.That(caught!.Message.Contains("GLOCTX", StringComparison.Ordinal)).IsFalse();
        }
        finally { File.Delete(tempFile); }
    }
}
