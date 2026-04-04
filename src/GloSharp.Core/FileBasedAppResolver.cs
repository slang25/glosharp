using System.Diagnostics;
using System.Text.Json;

namespace GloSharp.Core;

public class FileBasedAppResolveResult
{
    public required string AssetsFilePath { get; init; }
    public required string TargetFramework { get; init; }
}

public static class FileBasedAppResolver
{
    public static void EnsureSdkVersion()
    {
        var version = GetDotnetSdkVersion();
        if (version == null)
            throw new InvalidOperationException("Could not determine .NET SDK version. Ensure 'dotnet' is on the PATH.");

        if (version.Major < 10)
            throw new InvalidOperationException(
                $".NET 10+ SDK is required for file-based app directives (#:package, #:sdk, etc.), but found .NET {version}. " +
                "Either upgrade the SDK or use --project with a .csproj instead.");
    }

    public static Version? GetDotnetSdkVersion()
    {
        try
        {
            var psi = new ProcessStartInfo("dotnet", "--version")
            {
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
            };

            var process = Process.Start(psi);
            if (process == null) return null;

            var output = process.StandardOutput.ReadToEnd().Trim();
            process.WaitForExit();

            if (process.ExitCode != 0) return null;

            // Parse version like "10.0.100" or "11.0.100-preview.1.26104.118"
            var dashIndex = output.IndexOf('-');
            var versionPart = dashIndex >= 0 ? output[..dashIndex] : output;
            return Version.TryParse(versionPart, out var version) ? version : null;
        }
        catch
        {
            return null;
        }
    }

    public static FileBasedAppResolveResult BuildAndDiscoverAssets(string sourceFilePath, bool noRestore = false)
    {
        EnsureSdkVersion();

        var fullPath = Path.GetFullPath(sourceFilePath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException($"Source file not found: {fullPath}");

        // Step 1: Run dotnet build to restore packages and compile
        // (--getProperty alone evaluates the project without actually restoring)
        if (!noRestore)
        {
            RunDotnetBuild(fullPath);
        }

        // Step 2: Query project properties to discover the assets file location
        return QueryProjectProperties(fullPath);
    }

    private static void RunDotnetBuild(string fullPath)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{fullPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet build");

        process.StandardOutput.ReadToEnd(); // Drain stdout
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
            throw new InvalidOperationException(
                $"dotnet build failed for file-based app '{fullPath}':\n{stderr}");
    }

    private static FileBasedAppResolveResult QueryProjectProperties(string fullPath)
    {
        var psi = new ProcessStartInfo("dotnet", $"build \"{fullPath}\" --getProperty:ProjectAssetsFile --getProperty:TargetFramework")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        var process = Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start dotnet build --getProperty");

        var stdout = process.StandardOutput.ReadToEnd();
        process.StandardError.ReadToEnd(); // Drain stderr
        process.WaitForExit();

        return ParsePropertyOutput(stdout, fullPath);
    }

    public static ProjectAssetsResult ResolveReferences(string sourceFilePath, string? targetFramework = null, bool noRestore = false)
    {
        var buildResult = BuildAndDiscoverAssets(sourceFilePath, noRestore);
        return ProjectAssetsResolver.Resolve(
            buildResult.AssetsFilePath,
            targetFramework ?? buildResult.TargetFramework);
    }

    private static FileBasedAppResolveResult ParsePropertyOutput(string json, string sourceFilePath)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var properties = doc.RootElement.GetProperty("Properties");

            var assetsFile = properties.GetProperty("ProjectAssetsFile").GetString()
                ?? throw new InvalidOperationException("ProjectAssetsFile property was null");
            var tfm = properties.GetProperty("TargetFramework").GetString()
                ?? throw new InvalidOperationException("TargetFramework property was null");

            if (!File.Exists(assetsFile))
                throw new FileNotFoundException(
                    $"Generated project.assets.json not found at '{assetsFile}'. " +
                    "The SDK may not have restored successfully.");

            return new FileBasedAppResolveResult
            {
                AssetsFilePath = assetsFile,
                TargetFramework = tfm,
            };
        }
        catch (JsonException)
        {
            throw new InvalidOperationException(
                $"Failed to parse dotnet build property output for '{sourceFilePath}'. Output: {json}");
        }
    }
}
