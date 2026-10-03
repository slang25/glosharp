using GloSharp.Core;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Tests;

// U-proj F1/F5, U-node F16, R-core #20: --project resolution beyond NuGet compile assets.
public class ProjectReferenceResolutionTests
{
    private sealed class TempTree : IDisposable
    {
        // NuGet drops ProjectReferences from the assets file when restoring through a
        // symlinked path (macOS /var -> /private/var), so use the real temp path.
        public string Root { get; } = Path.Combine(RealTempPath(), $"gs-proj-{Guid.NewGuid():N}");

        private static string RealTempPath()
        {
            var temp = Path.GetTempPath();
            return OperatingSystem.IsMacOS() && (temp.StartsWith("/var/") || temp.StartsWith("/tmp/"))
                ? "/private" + temp
                : temp;
        }

        public TempTree() => Directory.CreateDirectory(Root);

        public string Write(string relative, string content)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, content);
            return path;
        }

        public string WriteBytes(string relative, byte[] content)
        {
            var path = Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content);
            return path;
        }

        public string P(string relative) => Path.Combine(Root, relative.Replace('/', Path.DirectorySeparatorChar));

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static string Json(string s) => s.Replace("\\", "\\\\");

    /// <summary>A minimal library with an XML doc comment, compiled to bytes.</summary>
    private static (byte[] Dll, string Xml) BuildLibrary(string assemblyName, string ns)
    {
        var source = $$"""
            namespace {{ns}};
            /// <summary>A widget from {{assemblyName}}.</summary>
            public class Widget { public int Size { get; set; } }
            """;
        var tpa = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(p => (MetadataReference)MetadataReference.CreateFromFile(p));
        var compilation = CSharpCompilation.Create(
            assemblyName,
            [CSharpSyntaxTree.ParseText(source, new CSharpParseOptions(documentationMode: DocumentationMode.Diagnose))],
            tpa,
            new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
        using var dll = new MemoryStream();
        using var xml = new MemoryStream();
        var emit = compilation.Emit(dll, xmlDocumentationStream: xml);
        if (!emit.Success) throw new InvalidOperationException(string.Join("\n", emit.Diagnostics));
        return (dll.ToArray(), System.Text.Encoding.UTF8.GetString(xml.ToArray()));
    }

    private static string WidgetDocs(ProjectAssetsResult result, string ns)
    {
        var compilation = CSharpCompilation.Create("probe",
            [CSharpSyntaxTree.ParseText($"class C {{ {ns}.Widget w; }}")],
            result.References.Concat(FrameworkResolver.GetFrameworkReferences("net8.0")));
        var type = compilation.GetTypeByMetadataName($"{ns}.Widget");
        return type?.GetDocumentationCommentXml() ?? "<type not found>";
    }

    [Test]
    public async Task Resolve_IncludesOwnBuildOutputAndProjectReferenceOutputs_WithXmlDocs()
    {
        using var tree = new TempTree();
        var samples = tree.Write("Samples/Samples.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        tree.Write("Acme.Widgets/Acme.Widgets.csproj",
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup><AssemblyName>Acme.Widgets</AssemblyName></PropertyGroup></Project>");

        var (libDll, libXml) = BuildLibrary("Acme.Widgets", "Acme");
        tree.WriteBytes("Acme.Widgets/bin/Debug/net8.0/Acme.Widgets.dll", libDll);
        tree.Write("Acme.Widgets/bin/Debug/net8.0/Acme.Widgets.xml", libXml);
        var (ownDll, ownXml) = BuildLibrary("Samples", "Samples");
        tree.WriteBytes("Samples/bin/Release/net8.0/Samples.dll", ownDll);
        tree.Write("Samples/bin/Release/net8.0/Samples.xml", ownXml);

        var json = $$"""
            {
              "version": 3,
              "targets": {
                "net8.0": {
                  "Acme.Widgets/1.0.0": {
                    "type": "project",
                    "framework": ".NETCoreApp,Version=v8.0",
                    "compile": { "bin/placeholder/Acme.Widgets.dll": {} }
                  }
                }
              },
              "libraries": {
                "Acme.Widgets/1.0.0": {
                  "type": "project",
                  "path": "../Acme.Widgets/Acme.Widgets.csproj",
                  "msbuildProject": "../Acme.Widgets/Acme.Widgets.csproj"
                }
              },
              "packageFolders": { "{{Json(tree.P("nuget"))}}": {} },
              "project": { "restore": { "projectPath": "{{Json(samples)}}" } }
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        // Project references are not packages (they were listed as if loaded before).
        await Assert.That(result.Packages.Count).IsEqualTo(0);
        await Assert.That(result.ProjectOutputPaths.Count).IsEqualTo(2);
        await Assert.That(result.Warnings.Count).IsEqualTo(0);
        await Assert.That(WidgetDocs(result, "Acme")).Contains("A widget from Acme.Widgets.");
        await Assert.That(WidgetDocs(result, "Samples")).Contains("A widget from Samples.");
    }

    [Test]
    public async Task Resolve_UnbuiltProjectReference_Warns()
    {
        using var tree = new TempTree();
        var samples = tree.Write("Samples/Samples.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");
        tree.Write("Lib/Lib.csproj", "<Project Sdk=\"Microsoft.NET.Sdk\" />");

        var json = $$"""
            {
              "version": 3,
              "targets": { "net8.0": { "Lib/1.0.0": { "type": "project", "framework": ".NETCoreApp,Version=v8.0" } } },
              "libraries": { "Lib/1.0.0": { "type": "project", "path": "../Lib/Lib.csproj", "msbuildProject": "../Lib/Lib.csproj" } },
              "packageFolders": { "{{Json(tree.P("nuget"))}}": {} },
              "project": { "restore": { "projectPath": "{{Json(samples)}}" } }
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.References.Count).IsEqualTo(0);
        await Assert.That(result.Warnings.Any(w => w.Contains("Project reference 'Lib'") && w.Contains("dotnet build"))).IsTrue();
        await Assert.That(result.Warnings.Any(w => w.Contains("Samples.csproj") && w.Contains("no build output"))).IsTrue();
    }

    [Test]
    public async Task Resolve_IncludeProjectOutputsFalse_SkipsProjectsSilently()
    {
        using var tree = new TempTree();
        var app = tree.Write("app.cs.csproj", "<Project />");
        var json = $$"""
            {
              "version": 3,
              "targets": { "net10.0": {} },
              "packageFolders": { "{{Json(tree.P("nuget"))}}": {} },
              "project": { "restore": { "projectPath": "{{Json(app)}}" } }
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json, includeProjectOutputs: false);

        await Assert.That(result.Warnings.Count).IsEqualTo(0);
        await Assert.That(result.ProjectOutputPaths.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Resolve_UsesLibraryPathAndFallbackPackageFolders()
    {
        using var tree = new TempTree();
        var (dll, _) = BuildLibrary("Pre.Release", "Pre");
        // Lives only in the second (fallback) folder, under the libraries[].path casing.
        tree.WriteBytes("fallback/pre.release/1.0.0-beta.1/lib/net8.0/Pre.Release.dll", dll);

        var json = $$"""
            {
              "version": 3,
              "targets": {
                "net8.0": {
                  "Pre.Release/1.0.0-Beta.1": {
                    "type": "package",
                    "compile": { "lib/net8.0/Pre.Release.dll": {} }
                  }
                }
              },
              "libraries": {
                "Pre.Release/1.0.0-Beta.1": { "type": "package", "path": "pre.release/1.0.0-beta.1" }
              },
              "packageFolders": {
                "{{Json(tree.P("primary"))}}": {},
                "{{Json(tree.P("fallback"))}}": {}
              }
            }
            """;
        Directory.CreateDirectory(tree.P("primary"));

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.References.Count).IsEqualTo(1);
        await Assert.That(result.Warnings.Count).IsEqualTo(0);
    }

    [Test]
    public async Task Resolve_MissingPackageFiles_Warns()
    {
        using var tree = new TempTree();
        Directory.CreateDirectory(tree.P("nuget"));
        var json = $$"""
            {
              "version": 3,
              "targets": { "net8.0": { "Gone/1.0.0": { "type": "package", "compile": { "lib/net8.0/Gone.dll": {} } } } },
              "packageFolders": { "{{Json(tree.P("nuget"))}}": {} }
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.Warnings.Any(w => w.Contains("Gone 1.0.0"))).IsTrue();
    }

    [Test]
    public async Task Resolve_FailedRestoreLogs_BecomeWarnings()
    {
        var json = """
            {
              "version": 3,
              "targets": { "net10.0": {} },
              "packageFolders": { "/nonexistent/nuget/": {} },
              "logs": [
                { "code": "NU1101", "level": "Error", "message": "Unable to find package Newtonsoft.Jsonnn." },
                { "code": "NU1603", "level": "Warning", "message": "approximate match" }
              ]
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.Warnings.Single()).Contains("NU1101: Unable to find package Newtonsoft.Jsonnn.");
    }

    [Test]
    public async Task GetRestoreReason_DetectsMissingStaleAndFailedRestores()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("App/App.csproj", "<Project />");

        await Assert.That(ProjectAssetsResolver.GetRestoreReason(csproj)).Contains("missing");

        var assets = tree.Write("App/obj/project.assets.json", """{ "version": 3, "logs": [] }""");
        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(-5));
        await Assert.That(ProjectAssetsResolver.GetRestoreReason(csproj)).IsNull();

        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(5));
        await Assert.That(ProjectAssetsResolver.GetRestoreReason(csproj)).Contains("changed");

        File.SetLastWriteTimeUtc(csproj, DateTime.UtcNow.AddMinutes(-5));
        File.WriteAllText(assets, """{ "version": 3, "logs": [ { "code": "NU1101", "level": "Error", "message": "x" } ] }""");
        await Assert.That(ProjectAssetsResolver.GetRestoreReason(csproj)).Contains("failed");
    }

    [Test]
    public async Task Resolve_CollectsFrameworkReferences_ForWebSdkProjects()
    {
        var json = """
            {
              "version": 3,
              "targets": {
                "net10.0": {
                  "Some.Lib/1.0.0": { "type": "package", "frameworkReferences": ["Microsoft.AspNetCore.App"] }
                }
              },
              "packageFolders": { "/nonexistent/nuget/": {} },
              "project": {
                "frameworks": {
                  "net10.0": {
                    "targetAlias": "net10.0",
                    "frameworkReferences": {
                      "Microsoft.AspNetCore.App": { "privateAssets": "none" },
                      "Microsoft.NETCore.App": { "privateAssets": "all" }
                    }
                  }
                }
              }
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json);

        await Assert.That(result.FrameworkReferences).IsEquivalentTo(new[] { "Microsoft.AspNetCore.App" });
    }

    [Test]
    public async Task Resolve_MultiTarget_WarnsAndSkipsRidTargets()
    {
        var json = """
            {
              "version": 3,
              "targets": {
                "net10.0": {},
                "net10.0/osx-arm64": {},
                "net8.0": {}
              },
              "packageFolders": { "/nonexistent/nuget/": {} }
            }
            """;

        var result = ProjectAssetsResolver.ResolveFromJson(json);
        var chosen = ProjectAssetsResolver.ResolveFromJson(json, "NET8.0");

        await Assert.That(result.TargetFramework).IsEqualTo("net10.0");
        await Assert.That(result.Warnings.Single()).Contains("net10.0, net8.0");
        await Assert.That(chosen.TargetFramework).IsEqualTo("net8.0");
        await Assert.That(chosen.Warnings.Count).IsEqualTo(0);
    }

    [Test]
    [Arguments(".NETCoreApp,Version=v8.0", "net8.0")]
    [Arguments(".NETCoreApp,Version=v3.1", "netcoreapp3.1")]
    [Arguments(".NETStandard,Version=v2.0", "netstandard2.0")]
    [Arguments("net10.0", "net10.0")]
    public async Task ParseTfmFromKey_MapsLongForms(string key, string expected)
    {
        await Assert.That(ProjectAssetsResolver.ParseTfmFromKey(key)).IsEqualTo(expected);
    }

    // ---------- project path validation (U-proj F24) ----------

    [Test]
    [Arguments("All.slnx", "solution")]
    [Arguments("All.sln", "solution")]
    [Arguments("e.cs", "not a C# project")]
    public async Task FindProjectFile_RejectsNonProjects(string fileName, string expected)
    {
        using var tree = new TempTree();
        var path = tree.Write(fileName, "");

        var ex = Assert.Throws<ArgumentException>(() => ProjectAssetsResolver.FindProjectFile(path));
        await Assert.That(ex.Message).Contains(expected);
    }

    [Test]
    public async Task FindProjectFile_DirectoryWithOneProject_ReturnsIt()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("App/App.csproj", "<Project />");

        await Assert.That(ProjectAssetsResolver.FindProjectFile(tree.P("App"))).IsEqualTo(csproj);
        await Assert.That(() => ProjectAssetsResolver.FindProjectFile(tree.P("missing"))).Throws<FileNotFoundException>();
    }

    [Test]
    public async Task FindAssetsFile_ArtifactsLayout()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("src/App/App.csproj", "<Project />");
        var assets = tree.Write("artifacts/obj/App/project.assets.json", "{}");

        await Assert.That(ProjectAssetsResolver.FindAssetsFile(csproj)).IsEqualTo(assets);
    }

    [Test]
    public async Task ProjectOutputLocator_ArtifactsLayout_AndNewestBuildWins()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("src/App/App.csproj", "<Project />");
        var debug = tree.Write("src/App/bin/Debug/net8.0/App.dll", "x");
        var release = tree.Write("artifacts/bin/App/release_net8.0/App.dll", "x");
        tree.Write("artifacts/bin/App/release_net9.0/App.dll", "x");
        File.SetLastWriteTimeUtc(debug, DateTime.UtcNow.AddHours(-1));
        File.SetLastWriteTimeUtc(release, DateTime.UtcNow);

        await Assert.That(ProjectOutputLocator.Find(csproj, "net8.0")).IsEqualTo(release);

        File.SetLastWriteTimeUtc(debug, DateTime.UtcNow.AddHours(1));
        await Assert.That(ProjectOutputLocator.Find(csproj, "net8.0")).IsEqualTo(debug);
        await Assert.That(ProjectOutputLocator.Find(csproj, "net7.0")).IsNull();
    }

    [Test]
    public async Task ProjectOutputLocator_UsesLiteralAssemblyName()
    {
        using var tree = new TempTree();
        var csproj = tree.Write("App/App.csproj",
            "<Project><PropertyGroup><AssemblyName>Contoso.App</AssemblyName></PropertyGroup></Project>");
        var dll = tree.Write("App/bin/Debug/net8.0/Contoso.App.dll", "x");

        await Assert.That(ProjectOutputLocator.Find(csproj, "net8.0")).IsEqualTo(dll);
    }

    // ---------- end to end against a real project ----------

    [Test]
    [RequiresDotnet10Sdk]
    public async Task Resolve_RealBuiltProjectReference_ProvidesOwnTypes()
    {
        using var tree = new TempTree();
        tree.Write("Lib/Lib.csproj", """
            <Project Sdk="Microsoft.NET.Sdk">
              <PropertyGroup>
                <TargetFramework>net8.0</TargetFramework>
                <GenerateDocumentationFile>true</GenerateDocumentationFile>
                <NoWarn>CS1591</NoWarn>
              </PropertyGroup>
            </Project>
            """);
        tree.Write("Lib/Widget.cs", "namespace Lib;\n/// <summary>Lib widget.</summary>\npublic class Widget { }\n");
        var app = tree.Write("App/App.csproj", """
            <Project Sdk="Microsoft.NET.Sdk.Web">
              <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
              <ItemGroup><ProjectReference Include="../Lib/Lib.csproj" /></ItemGroup>
            </Project>
            """);

        var build = await ProcessRunner.RunAsync(FrameworkResolver.GetDotnetExecutable(), ["build", tree.P("Lib/Lib.csproj"), "-nologo"]);
        build.EnsureSuccess("building Lib");
        var restore = await ProcessRunner.RunAsync(FrameworkResolver.GetDotnetExecutable(), ["restore", app, "-nologo"]);
        restore.EnsureSuccess("restoring App");

        var result = ProjectAssetsResolver.Resolve(ProjectAssetsResolver.FindAssetsFile(app));

        await Assert.That(WidgetDocs(result, "Lib")).Contains("Lib widget.");
        await Assert.That(result.FrameworkReferences).Contains("Microsoft.AspNetCore.App");
        await Assert.That(result.Packages.Count).IsEqualTo(0);
        // App itself was never built.
        await Assert.That(result.Warnings.Any(w => w.Contains("App.csproj"))).IsTrue();

        // And the framework reference resolves to the ASP.NET Core targeting pack.
        var refs = FrameworkResolver.GetFrameworkReferences("net8.0", result.FrameworkReferences);
        await Assert.That(refs.Any(r => r.Display?.EndsWith("Microsoft.AspNetCore.dll", StringComparison.Ordinal) == true)).IsTrue();
    }
}
