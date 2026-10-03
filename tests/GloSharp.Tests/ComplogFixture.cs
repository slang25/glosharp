using Basic.CompilerLog.Util;
using GloSharp.Core;

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

            ProcessRunner.Run(
                    FrameworkResolver.GetDotnetExecutable(),
                    ["build", Path.Combine(srcRoot, "Both.slnx"), $"-bl:{binlogPath}", "-v:q"],
                    srcRoot,
                    TimeSpan.FromMinutes(10))
                .EnsureSuccess("Building the MultiProject complog fixture");

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
}
