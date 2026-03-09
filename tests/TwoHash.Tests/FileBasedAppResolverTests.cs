using TwoHash.Core;

namespace TwoHash.Tests;

public class FileBasedAppResolverTests
{
    [Test]
    public async Task GetDotnetSdkVersion_ReturnsVersion()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();

        await Assert.That(version).IsNotNull();
        await Assert.That(version!.Major).IsGreaterThanOrEqualTo(8);
    }

    [Test]
    public async Task EnsureSdkVersion_DoesNotThrow_WhenSdkIs10OrLater()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();
        if (version == null || version.Major < 10)
        {
            // Skip test if .NET 10+ SDK is not installed
            return;
        }

        // Should not throw
        FileBasedAppResolver.EnsureSdkVersion();
        await Assert.That(true).IsTrue();
    }

    [Test]
    public async Task BuildAndDiscoverAssets_ResolvesPackage()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();
        if (version == null || version.Major < 10)
            return; // Skip if .NET 10+ not available

        // Create a temp file-based app
        var tempDir = Path.Combine(Path.GetTempPath(), $"twohash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.cs");

        try
        {
            File.WriteAllText(filePath, "#:package Newtonsoft.Json@13.0.3\nConsole.WriteLine(\"hello\");");

            var result = FileBasedAppResolver.BuildAndDiscoverAssets(filePath);

            await Assert.That(result.AssetsFilePath).IsNotNull();
            await Assert.That(File.Exists(result.AssetsFilePath)).IsTrue();
            await Assert.That(result.TargetFramework).IsNotNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task ResolveReferences_ReturnsReferencesForPackage()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();
        if (version == null || version.Major < 10)
            return; // Skip if .NET 10+ not available

        var tempDir = Path.Combine(Path.GetTempPath(), $"twohash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);
        var filePath = Path.Combine(tempDir, "test.cs");

        try
        {
            File.WriteAllText(filePath, "#:package Newtonsoft.Json@13.0.3\nConsole.WriteLine(\"hello\");");

            var result = FileBasedAppResolver.ResolveReferences(filePath);

            await Assert.That(result.References.Count).IsGreaterThan(0);
            await Assert.That(result.Packages.Count).IsGreaterThan(0);
            await Assert.That(result.Packages.Any(p => p.Name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task BuildAndDiscoverAssets_ThrowsForNonexistentFile()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();
        if (version == null || version.Major < 10)
            return;

        await Assert.That(() => FileBasedAppResolver.BuildAndDiscoverAssets("/nonexistent/file.cs"))
            .Throws<FileNotFoundException>();
    }
}
