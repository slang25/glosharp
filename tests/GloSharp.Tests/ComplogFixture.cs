using System.Diagnostics;
using Basic.CompilerLog.Util;

namespace GloSharp.Tests;

internal static class ComplogFixture
{
    private static readonly object _lock = new();

    public static string GetOrBuildMultiProjectComplog()
    {
        var cacheDir = Path.Combine(AppContext.BaseDirectory, "complogs");
        var complogPath = Path.Combine(cacheDir, "multiproject.complog");

        lock (_lock)
        {
            if (File.Exists(complogPath))
                return complogPath;

            Directory.CreateDirectory(cacheDir);

            var srcRoot = FindFixtureSource("MultiProject");
            var binlogPath = Path.Combine(cacheDir, "multiproject.binlog");

            // Clean any prior obj/bin under the fixture source so the binlog is deterministic.
            foreach (var sub in new[] { "ProjA/obj", "ProjA/bin", "ProjB/obj", "ProjB/bin" })
            {
                var p = Path.Combine(srcRoot, sub);
                if (Directory.Exists(p)) Directory.Delete(p, recursive: true);
            }

            RunProcess("dotnet", $"build \"{Path.Combine(srcRoot, "Both.slnx")}\" -bl:\"{binlogPath}\" -v:q", srcRoot);

            var conversion = CompilerLogUtil.TryConvertBinaryLog(binlogPath, complogPath);
            if (!conversion.Succeeded)
                throw new InvalidOperationException(
                    $"Binlog conversion failed: {string.Join("; ", conversion.Diagnostics)}");

            return complogPath;
        }
    }

    private static string FindFixtureSource(string name)
    {
        var dir = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(dir))
        {
            var candidate = Path.Combine(dir, "fixtures", "complogs", name);
            if (Directory.Exists(candidate)) return candidate;
            dir = Path.GetDirectoryName(dir);
        }
        throw new DirectoryNotFoundException(
            $"Could not locate test fixture source 'fixtures/complogs/{name}' relative to {AppContext.BaseDirectory}");
    }

    private static void RunProcess(string fileName, string arguments, string workingDir)
    {
        var psi = new ProcessStartInfo(fileName, arguments)
        {
            WorkingDirectory = workingDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        using var proc = Process.Start(psi)
            ?? throw new InvalidOperationException($"Could not start {fileName}");

        var stdout = proc.StandardOutput.ReadToEnd();
        var stderr = proc.StandardError.ReadToEnd();
        proc.WaitForExit();

        if (proc.ExitCode != 0)
            throw new InvalidOperationException(
                $"{fileName} {arguments} exited with code {proc.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
    }
}
