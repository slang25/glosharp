using TwoHash.Core;

namespace TwoHash.Tests;

public class ProjectAssetsResolverTests
{
    private static string MakeAssetsJson(
        string targets,
        string packageFolders = "\"/Users/test/.nuget/packages/\": {}")
    {
        return $$$"""
        {
          "version": 3,
          "targets": { {{{targets}}} },
          "packageFolders": { {{{packageFolders}}} }
        }
        """;
    }

    [Test]
    public async Task ResolveFromJson_SinglePackage_ResolvesAssembly()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "Newtonsoft.Json/13.0.3": {
                "type": "package",
                "compile": {
                  "lib/net6.0/Newtonsoft.Json.dll": {}
                }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.Packages.Count).IsEqualTo(1);
        await Assert.That(result.Packages[0].Name).IsEqualTo("Newtonsoft.Json");
        await Assert.That(result.Packages[0].Version).IsEqualTo("13.0.3");
        await Assert.That(result.TargetFramework).IsEqualTo("net8.0");
    }

    [Test]
    public async Task ResolveFromJson_TransitiveDeps_ResolvesAll()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "PackageA/1.0.0": {
                "type": "package",
                "dependencies": { "PackageB": "2.0.0" },
                "compile": {
                  "lib/net8.0/PackageA.dll": {}
                }
              },
              "PackageB/2.0.0": {
                "type": "package",
                "compile": {
                  "lib/net8.0/PackageB.dll": {}
                }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.Packages.Count).IsEqualTo(2);
        await Assert.That(result.Packages[0].Name).IsEqualTo("PackageA");
        await Assert.That(result.Packages[1].Name).IsEqualTo("PackageB");
    }

    [Test]
    public async Task ResolveFromJson_MissingCompileAssets_SkipsPackage()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "Analyzers.Only/1.0.0": {
                "type": "package",
                "build": {
                  "buildTransitive/Analyzers.props": {}
                }
              },
              "Real.Package/1.0.0": {
                "type": "package",
                "compile": {
                  "lib/net8.0/Real.Package.dll": {}
                }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.Packages.Count).IsEqualTo(1);
        await Assert.That(result.Packages[0].Name).IsEqualTo("Real.Package");
    }

    [Test]
    public async Task ResolveFromJson_PlaceholderCompileEntry_FallsBackToRuntime()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "Humanizer.Core/2.14.1": {
                "type": "package",
                "compile": {
                  "lib/net6.0/_._": {}
                },
                "runtime": {
                  "lib/net6.0/Humanizer.dll": {}
                }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.Packages.Count).IsEqualTo(1);
        await Assert.That(result.Packages[0].Name).IsEqualTo("Humanizer.Core");
    }

    [Test]
    public async Task ResolveFromJson_CustomPackageFolder_UsesCustomPath()
    {
        var json = MakeAssetsJson(
            targets: """
                "net8.0": {
                  "MyPkg/1.0.0": {
                    "type": "package",
                    "compile": {
                      "lib/net8.0/MyPkg.dll": {}
                    }
                  }
                }
            """,
            packageFolders: "\"/opt/nuget/cache/\": {}");

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        // The references won't exist on disk so count will be 0 for MetadataReference,
        // but packages metadata should still be extracted
        await Assert.That(result.Packages.Count).IsEqualTo(1);
        await Assert.That(result.Packages[0].Name).IsEqualTo("MyPkg");
    }

    [Test]
    public async Task ResolveFromJson_ExplicitTfm_SelectsCorrectTarget()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "PkgA/1.0.0": {
                "type": "package",
                "compile": { "lib/net8.0/PkgA.dll": {} }
              }
            },
            ".NETCoreApp,Version=v9.0": {
              "PkgB/2.0.0": {
                "type": "package",
                "compile": { "lib/net9.0/PkgB.dll": {} }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json, targetFramework: "net9.0");

        await Assert.That(result.TargetFramework).IsEqualTo("net9.0");
        await Assert.That(result.Packages.Count).IsEqualTo(1);
        await Assert.That(result.Packages[0].Name).IsEqualTo("PkgB");
    }

    [Test]
    public async Task ResolveFromJson_DefaultTfm_SelectsFirstTarget()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "PkgA/1.0.0": {
                "type": "package",
                "compile": { "lib/net8.0/PkgA.dll": {} }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.TargetFramework).IsEqualTo("net8.0");
    }

    [Test]
    public void ResolveFromJson_TfmNotFound_Throws()
    {
        var json = MakeAssetsJson("""
            "net8.0": {
              "PkgA/1.0.0": {
                "type": "package",
                "compile": { "lib/net8.0/PkgA.dll": {} }
              }
            }
        """);

        Assert.Throws<InvalidOperationException>(() =>
            ProjectAssetsResolver.ResolveFromJson(json, targetFramework: "net9.0"));
    }

    [Test]
    public async Task ResolveFromJson_LongFormTfmKey_ParsesCorrectly()
    {
        var json = MakeAssetsJson("""
            ".NETCoreApp,Version=v8.0": {
              "SomePkg/1.0.0": {
                "type": "package",
                "compile": { "lib/net8.0/SomePkg.dll": {} }
              }
            }
        """);

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.TargetFramework).IsEqualTo("net8.0");
        await Assert.That(result.Packages.Count).IsEqualTo(1);
    }

    [Test]
    public async Task FindAssetsFile_WithCsprojPath_ResolvesObjDir()
    {
        // Create a temp directory structure
        var tempDir = Path.Combine(Path.GetTempPath(), $"twohash-test-{Guid.NewGuid():N}");
        var objDir = Path.Combine(tempDir, "obj");
        Directory.CreateDirectory(objDir);

        var csproj = Path.Combine(tempDir, "Test.csproj");
        var assets = Path.Combine(objDir, "project.assets.json");
        File.WriteAllText(csproj, "<Project/>");
        File.WriteAllText(assets, "{}");

        try
        {
            var result = ProjectAssetsResolver.FindAssetsFile(csproj);
            await Assert.That(result).IsEqualTo(assets);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public async Task FindAssetsFile_WithDirectoryPath_ResolvesObjDir()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"twohash-test-{Guid.NewGuid():N}");
        var objDir = Path.Combine(tempDir, "obj");
        Directory.CreateDirectory(objDir);

        var assets = Path.Combine(objDir, "project.assets.json");
        File.WriteAllText(assets, "{}");

        try
        {
            var result = ProjectAssetsResolver.FindAssetsFile(tempDir);
            await Assert.That(result).IsEqualTo(assets);
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }

    [Test]
    public void FindAssetsFile_MissingAssets_Throws()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), $"twohash-test-{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            Assert.Throws<FileNotFoundException>(() =>
                ProjectAssetsResolver.FindAssetsFile(tempDir));
        }
        finally
        {
            Directory.Delete(tempDir, true);
        }
    }
}
