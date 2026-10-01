using GloSharp.Core;

namespace GloSharp.Tests;

public class FrameworkResolverTests
{
    /// <summary>A fake dotnet root with empty Microsoft.NETCore.App.Ref packs.</summary>
    private sealed class FakeDotnetRoot : IDisposable
    {
        public string Root { get; } = Path.Combine(Path.GetTempPath(), $"gs-dotnet-{Guid.NewGuid():N}");

        public FakeDotnetRoot AddPack(string version, string tfm, string pack = "Microsoft.NETCore.App.Ref")
        {
            var dir = Path.Combine(Root, "packs", pack, version, "ref", tfm);
            Directory.CreateDirectory(dir);
            File.WriteAllText(Path.Combine(dir, "marker.txt"), $"{pack} {version}");
            return this;
        }

        public void Dispose()
        {
            if (Directory.Exists(Root)) Directory.Delete(Root, recursive: true);
        }
    }

    private static string VersionOf(string refDir) =>
        Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(refDir))!);

    // ---------- version ordering (R-core #4b, U-proj F9, U-cli F14) ----------

    [Test]
    [Arguments("10.0.12", "10.0.7")]
    [Arguments("10.0.10", "10.0.9")]
    [Arguments("10.0.0", "9.0.3")]
    [Arguments("11.0.0-rc.1.26451.104", "10.0.12")]
    [Arguments("11.0.0", "11.0.0-rc.1.26451.104")]
    [Arguments("11.0.0-rc.1.26451.104", "11.0.0-preview.7.26381.103")]
    [Arguments("8.0.0-rc.10", "8.0.0-rc.2")]
    [Arguments("8.0.0-rc.1", "8.0.0-rc")]
    public async Task PackVersion_OrdersSemantically(string higher, string lower)
    {
        await Assert.That(PackVersion.TryParse(higher, out var h)).IsTrue();
        await Assert.That(PackVersion.TryParse(lower, out var l)).IsTrue();
        await Assert.That(h.CompareTo(l)).IsGreaterThan(0);
        await Assert.That(l.CompareTo(h)).IsLessThan(0);
    }

    [Test]
    public async Task ResolvePackRefPath_PicksHighestPatchBySemver()
    {
        using var root = new FakeDotnetRoot()
            .AddPack("10.0.7", "net10.0")
            .AddPack("10.0.12", "net10.0")
            .AddPack("10.0.9", "net10.0")
            .AddPack("9.0.3", "net9.0");

        var path = FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.NETCore.App.Ref", "net10.0");

        await Assert.That(VersionOf(path)).IsEqualTo("10.0.12");
        await Assert.That(Path.GetFileName(path)).IsEqualTo("net10.0");
    }

    [Test]
    public async Task ResolvePackRefPath_NullTfm_UsesNewestFramework_NotStringSorted()
    {
        using var root = new FakeDotnetRoot()
            .AddPack("9.0.3", "net9.0")
            .AddPack("10.0.12", "net10.0")
            .AddPack("11.0.0-rc.1.1", "net11.0");

        var path = FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.NETCore.App.Ref", null);

        await Assert.That(Path.GetFileName(path)).IsEqualTo("net11.0");
    }

    [Test]
    public async Task ResolvePackRefPath_PrefersStableOverPrereleaseOfSameVersion()
    {
        using var root = new FakeDotnetRoot()
            .AddPack("11.0.0-rc.2.1", "net11.0")
            .AddPack("11.0.0", "net11.0");

        var path = FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.NETCore.App.Ref", "net11.0");

        await Assert.That(VersionOf(path)).IsEqualTo("11.0.0");
    }

    // ---------- TFM validation (R-core #4c, U-proj F9, U-cli F14) ----------

    [Test]
    [Arguments("NET10.0")]
    [Arguments("Net10.0")]
    [Arguments("net10.0-windows")]
    public async Task ResolvePackRefPath_TfmIsCaseInsensitive_AndIgnoresPlatform(string tfm)
    {
        using var root = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");

        var path = FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.NETCore.App.Ref", tfm);

        await Assert.That(Path.GetFileName(path)).IsEqualTo("net10.0");
    }

    [Test]
    public async Task ResolvePackRefPath_NotInstalledTfm_ThrowsListingInstalled_InsteadOfFallingBack()
    {
        using var root = new FakeDotnetRoot()
            .AddPack("9.0.3", "net9.0")
            .AddPack("10.0.1", "net10.0");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.NETCore.App.Ref", "net99.0"));

        await Assert.That(ex.Message).Contains("'net99.0' is not installed");
        await Assert.That(ex.Message).Contains("Installed: net9.0, net10.0");
    }

    [Test]
    [Arguments("banana", "not a valid target framework")]
    [Arguments("netstandard2.0", ".NET Standard")]
    [Arguments("net48", ".NET Framework")]
    [Arguments("net472", ".NET Framework")]
    [Arguments("net4.0", "not a valid")]
    [Arguments("net10", "Did you mean net10.0?")]
    [Arguments("", "empty")]
    public async Task ParseTargetFramework_RejectsUnsupported(string tfm, string expected)
    {
        var ex = Assert.Throws<InvalidOperationException>(() => FrameworkResolver.ParseTargetFramework(tfm));
        await Assert.That(ex.Message).Contains(expected);
    }

    [Test]
    [Arguments("net8.0", "net8.0")]
    [Arguments("NET10.0", "net10.0")]
    [Arguments("netcoreapp3.1", "netcoreapp3.1")]
    [Arguments("net8.0-Windows", "net8.0-windows")]
    public async Task NormalizeTargetFramework_ReturnsCanonicalShortName(string tfm, string expected)
    {
        await Assert.That(FrameworkResolver.NormalizeTargetFramework(tfm)).IsEqualTo(expected);
    }

    [Test]
    public async Task ResolvePackRefPath_AdditionalPackMissing_ThrowsNamingFrameworkReference()
    {
        using var root = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");

        var ex = Assert.Throws<InvalidOperationException>(() =>
            FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.AspNetCore.App.Ref", "net10.0"));

        await Assert.That(ex.Message).Contains("Microsoft.AspNetCore.App");
    }

    [Test]
    public async Task ResolvePackRefPath_PackDirectoryMatchedCaseInsensitively()
    {
        using var root = new FakeDotnetRoot().AddPack("10.0.1", "net10.0", pack: "microsoft.aspnetcore.app.ref");

        var path = FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.AspNetCore.App.Ref", "net10.0");

        await Assert.That(Path.GetFileName(path)).IsEqualTo("net10.0");
    }

    [Test]
    public async Task ResolvePackRefPath_AlsoUsesPacksRestoredIntoNuGetFolder()
    {
        // e.g. a net8.0 project restored with an SDK that doesn't bundle the 8.0 pack.
        using var root = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");
        using var nuget = new FakeDotnetRoot().AddPack("8.0.24", "net8.0", pack: "microsoft.netcore.app.ref");
        var nugetFolder = Path.Combine(nuget.Root, "packs");

        var path = FrameworkResolver.ResolvePackRefPath(root.Root, "Microsoft.NETCore.App.Ref", "net8.0", [nugetFolder]);

        await Assert.That(VersionOf(path)).IsEqualTo("8.0.24");
    }

    [Test]
    [Arguments("Microsoft.AspNetCore.App", "Microsoft.AspNetCore.App.Ref")]
    [Arguments("Microsoft.AspNetCore.App.Ref", "Microsoft.AspNetCore.App.Ref")]
    public async Task ToRefPackName_MapsFrameworkReferenceNames(string input, string expected)
    {
        await Assert.That(FrameworkResolver.ToRefPackName(input)).IsEqualTo(expected);
    }

    // ---------- dotnet root discovery (R-core #4a, U-proj F0) ----------

    private static DotnetRootProbe Probe(
        Dictionary<string, string>? env = null,
        string? onPath = null,
        string[]? known = null,
        string? home = null) => new()
        {
            GetEnvironmentVariable = name => env != null && env.TryGetValue(name, out var v) ? v : null,
            FindOnPath = _ => onPath,
            KnownLocations = known ?? [],
            HomeDirectory = home,
        };

    [Test]
    public async Task FindDotnetRoot_HomeDotnetWithOnlyTools_IsSkipped()
    {
        var home = Path.Combine(Path.GetTempPath(), $"gs-home-{Guid.NewGuid():N}");
        Directory.CreateDirectory(Path.Combine(home, ".dotnet", "tools")); // what `dotnet tool install -g` creates
        using var real = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");
        try
        {
            var found = FrameworkResolver.FindDotnetRoot(Probe(known: [real.Root], home: home));
            await Assert.That(found).IsEqualTo(Path.GetFullPath(real.Root));
        }
        finally
        {
            Directory.Delete(home, recursive: true);
        }
    }

    [Test]
    public async Task FindDotnetRoot_DotnetRootWithoutPacks_FallsThrough()
    {
        var empty = Path.Combine(Path.GetTempPath(), $"gs-empty-{Guid.NewGuid():N}");
        Directory.CreateDirectory(empty);
        using var real = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");
        try
        {
            var found = FrameworkResolver.FindDotnetRoot(Probe(
                env: new() { ["DOTNET_ROOT"] = empty },
                known: [real.Root]));
            await Assert.That(found).IsEqualTo(Path.GetFullPath(real.Root));
        }
        finally
        {
            Directory.Delete(empty, recursive: true);
        }
    }

    [Test]
    public async Task FindDotnetRoot_PrefersDotnetRoot_ThenPath_ThenKnownLocations()
    {
        using var viaEnv = new FakeDotnetRoot().AddPack("8.0.1", "net8.0");
        using var viaPath = new FakeDotnetRoot().AddPack("9.0.1", "net9.0");
        using var viaKnown = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");
        var dotnetOnPath = Path.Combine(viaPath.Root, "dotnet");
        File.WriteAllText(dotnetOnPath, "");

        var withEnv = FrameworkResolver.FindDotnetRoot(Probe(
            env: new() { ["DOTNET_ROOT"] = viaEnv.Root }, onPath: dotnetOnPath, known: [viaKnown.Root]));
        var withPath = FrameworkResolver.FindDotnetRoot(Probe(onPath: dotnetOnPath, known: [viaKnown.Root]));
        var withKnown = FrameworkResolver.FindDotnetRoot(Probe(known: [viaKnown.Root]));

        await Assert.That(withEnv).IsEqualTo(Path.GetFullPath(viaEnv.Root));
        await Assert.That(withPath).IsEqualTo(Path.GetFullPath(viaPath.Root));
        await Assert.That(withKnown).IsEqualTo(Path.GetFullPath(viaKnown.Root));
    }

    [Test]
    public async Task FindDotnetRoot_FollowsSymlinkedDotnetOnPath()
    {
        if (OperatingSystem.IsWindows())
            return; // symlink creation needs privileges on Windows

        using var real = new FakeDotnetRoot().AddPack("10.0.1", "net10.0");
        var realDotnet = Path.Combine(real.Root, "dotnet");
        File.WriteAllText(realDotnet, "");
        var binDir = Path.Combine(Path.GetTempPath(), $"gs-bin-{Guid.NewGuid():N}");
        Directory.CreateDirectory(binDir);
        try
        {
            var link = Path.Combine(binDir, "dotnet");
            File.CreateSymbolicLink(link, realDotnet); // like /usr/local/bin/dotnet

            var found = FrameworkResolver.FindDotnetRoot(Probe(onPath: link));

            await Assert.That(found).IsEqualTo(Path.GetFullPath(real.Root));
        }
        finally
        {
            Directory.Delete(binDir, recursive: true);
        }
    }

    [Test]
    public async Task FindDotnetRoot_NothingUsable_ReturnsNull()
    {
        var found = FrameworkResolver.FindDotnetRoot(Probe(known: ["/definitely/not/here"], home: "/nor/here"));
        await Assert.That(found).IsNull();
    }

    [Test]
    public async Task DefaultKnownLocations_IncludeDistroAndSystemInstalls()
    {
        var locations = DotnetRootProbe.DefaultKnownLocations();
        if (OperatingSystem.IsLinux())
        {
            await Assert.That(locations).Contains("/usr/lib/dotnet");
            await Assert.That(locations).Contains("/usr/share/dotnet");
        }
        else if (OperatingSystem.IsMacOS())
        {
            await Assert.That(locations).Contains("/usr/local/share/dotnet");
        }
        else if (OperatingSystem.IsWindows())
        {
            await Assert.That(locations.Any(l => l.EndsWith(Path.Combine("Program Files", "dotnet"), StringComparison.OrdinalIgnoreCase))).IsTrue();
        }
    }

    // ---------- against the real SDK ----------

    [Test]
    public async Task FindDotnetRoot_OnThisMachine_HasNetCorePacks()
    {
        var root = FrameworkResolver.FindDotnetRoot();
        await Assert.That(root).IsNotNull();
        await Assert.That(Directory.Exists(Path.Combine(root!, "packs", "Microsoft.NETCore.App.Ref"))).IsTrue();
    }

    [Test]
    public async Task GetFrameworkReferences_Net8_LoadsXmlDocs()
    {
        var refs = FrameworkResolver.GetFrameworkReferences("net8.0");
        await Assert.That(refs.Count).IsGreaterThan(100);
        await Assert.That(refs.Any(r => r.Display?.EndsWith("System.Runtime.dll", StringComparison.Ordinal) == true)).IsTrue();
    }
}
