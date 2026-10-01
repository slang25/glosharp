using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>Skips a test unless the default `dotnet` is a .NET 10+ SDK (file-based apps).</summary>
public sealed class RequiresDotnet10SdkAttribute()
    : SkipAttribute(".NET 10+ SDK is required for file-based apps (#:package)")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context)
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();
        return Task.FromResult(version == null || version.Major < 10);
    }
}

public class FileBasedAppResolverTests
{
    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"glosharp test {Guid.NewGuid():N}"); // space on purpose
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Test]
    public async Task GetDotnetSdkVersion_ReturnsVersion()
    {
        var version = FileBasedAppResolver.GetDotnetSdkVersion();

        await Assert.That(version).IsNotNull();
        await Assert.That(version!.Major).IsGreaterThanOrEqualTo(8);
    }

    [Test]
    [RequiresDotnet10Sdk]
    public async Task EnsureSdkVersion_DoesNotThrow_WhenSdkIs10OrLater()
    {
        await Assert.That(() => FileBasedAppResolver.EnsureSdkVersion()).ThrowsNothing();
    }

    [Test]
    [RequiresDotnet10Sdk]
    public async Task RestoreAndDiscoverAssets_ResolvesPackage()
    {
        var tempDir = NewTempDir();
        var filePath = Path.Combine(tempDir, "test.cs");

        try
        {
            File.WriteAllText(filePath, "#:package Newtonsoft.Json@13.0.3\nConsole.WriteLine(\"hello\");");

            var result = FileBasedAppResolver.RestoreAndDiscoverAssets(filePath);

            await Assert.That(File.Exists(result.AssetsFilePath)).IsTrue();
            await Assert.That(result.TargetFramework).IsNotNull();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [RequiresDotnet10Sdk]
    public async Task ResolveReferences_ReturnsReferencesForPackage()
    {
        var tempDir = NewTempDir();
        var filePath = Path.Combine(tempDir, "test.cs");

        try
        {
            File.WriteAllText(filePath, "#:package Newtonsoft.Json@13.0.3\nConsole.WriteLine(\"hello\");");

            var result = FileBasedAppResolver.ResolveReferences(filePath);

            await Assert.That(result.References.Count).IsGreaterThan(0);
            await Assert.That(result.Packages.Any(p => p.Name.Equals("Newtonsoft.Json", StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    // R-core #3 / U-cli F7 / U-proj F8: a snippet that intentionally doesn't compile must
    // still get its packages — resolution restores, it never builds.
    [Test]
    [RequiresDotnet10Sdk]
    public async Task ResolveReferences_SnippetWithCompileErrors_StillResolvesPackages()
    {
        var tempDir = NewTempDir();
        var filePath = Path.Combine(tempDir, "witherr.cs");

        try
        {
            File.WriteAllText(filePath,
                "#:package Newtonsoft.Json@13.0.3\nusing Newtonsoft.Json;\n// @errors: CS0029\nint bad = \"x\";\n");

            var result = FileBasedAppResolver.ResolveReferences(filePath);

            await Assert.That(result.References.Any(r => r.Display?.EndsWith("Newtonsoft.Json.dll", StringComparison.OrdinalIgnoreCase) == true)).IsTrue();
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [RequiresDotnet10Sdk]
    public async Task ResolveReferences_NonexistentPackage_ThrowsWithNuGetError()
    {
        var tempDir = NewTempDir();
        var filePath = Path.Combine(tempDir, "bad.cs");

        try
        {
            File.WriteAllText(filePath, "#:package This.Package.Does.Not.Exist.GloSharpTest@1.0.0\nConsole.WriteLine(1);\n");

            var ex = Assert.Throws<InvalidOperationException>(() => FileBasedAppResolver.ResolveReferences(filePath));

            // MSBuild/NuGet write errors to stdout; the message must carry them.
            await Assert.That(ex.Message).Contains("NU1101");
            await Assert.That(ex.Message).Contains("This.Package.Does.Not.Exist.GloSharpTest");
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    [RequiresDotnet10Sdk]
    public async Task RestoreAndDiscoverAssets_ThrowsForNonexistentFile()
    {
        await Assert.That(() => FileBasedAppResolver.RestoreAndDiscoverAssets("/nonexistent/file.cs"))
            .Throws<FileNotFoundException>();
    }

    [Test]
    [RequiresDotnet10Sdk]
    public async Task ResolveReferencesForSource_ResolvesWithoutAFileOnDisk()
    {
        var result = FileBasedAppResolver.ResolveReferencesForSource(
            "#:package Newtonsoft.Json@13.0.3\nusing Newtonsoft.Json;\nint broken = \"x\";\n");

        await Assert.That(result.Packages.Any(p => p.Name == "Newtonsoft.Json")).IsTrue();
    }

    // R-core #16 / U-proj F15: stdin snippets used a new GUID file each time, leaking one SDK
    // artifacts directory per snippet. The directives file is now content-addressed.
    [Test]
    public async Task WriteDirectivesFile_IsStableForSameDirectives_AndIgnoresCode()
    {
        var root = NewTempDir();
        try
        {
            var a = FileBasedAppResolver.WriteDirectivesFile("#:package A@1.0.0\nvar x = 1;\n", root);
            var b = FileBasedAppResolver.WriteDirectivesFile("#:package A@1.0.0\r\nConsole.WriteLine(2);", root);
            var c = FileBasedAppResolver.WriteDirectivesFile("#:package A@2.0.0\nvar x = 1;\n", root);

            await Assert.That(a).IsEqualTo(b);
            await Assert.That(c).IsNotEqualTo(a);
            await Assert.That(File.ReadAllText(a)).IsEqualTo("#:package A@1.0.0\n");
            await Assert.That(Directory.GetDirectories(root).Length).IsEqualTo(2);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }

    [Test]
    public async Task ParsePropertyOutput_SkipsDiagnosticLinesBeforeJson()
    {
        var output = """
            /tmp/x.cs.csproj : warning NU1603: something approximate
            {
              "Properties": {
                "ProjectAssetsFile": "/tmp/obj/project.assets.json",
                "TargetFramework": "net10.0"
              }
            }
            """;

        var result = FileBasedAppResolver.ParsePropertyOutput(output, "/tmp/x.cs");

        await Assert.That(result.AssetsFilePath).IsEqualTo("/tmp/obj/project.assets.json");
        await Assert.That(result.TargetFramework).IsEqualTo("net10.0");
    }

    [Test]
    public async Task ParsePropertyOutput_GarbageThrowsWithOutput()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            FileBasedAppResolver.ParsePropertyOutput("MSBUILD : error MSB1009: Project file does not exist.", "/tmp/x.cs"));
        await Assert.That(ex.Message).Contains("MSB1009");
    }
}
