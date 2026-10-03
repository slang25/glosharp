using System.Diagnostics;
using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>Skips a test on Windows (it drives /bin/sh).</summary>
public sealed class RequiresPosixShellAttribute() : SkipAttribute("Requires /bin/sh")
{
    public override Task<bool> ShouldSkip(TestRegisteredContext context) =>
        Task.FromResult(OperatingSystem.IsWindows() || !File.Exists("/bin/sh"));
}

// R-core #9: one runner with ArgumentList, concurrent draining, timeout + kill tree.
public class ProcessRunnerTests
{
    [Test]
    [RequiresPosixShell]
    public async Task Run_LargeOutputOnBothStreams_DoesNotDeadlock()
    {
        // ~1 MB on each stream: far beyond the pipe buffer, which deadlocked the old
        // "WaitForExit, then read" and "read stdout to end, then stderr" code.
        var script = "i=0; while [ $i -lt 20000 ]; do echo \"out $i padding-padding-padding-padding\"; echo \"err $i padding-padding-padding-padding\" >&2; i=$((i+1)); done";

        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", script], timeout: TimeSpan.FromMinutes(2));

        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.StandardOutput).Contains("out 19999");
        await Assert.That(result.StandardError).Contains("err 19999");
        await Assert.That(result.CombinedOutput).Contains("out 19999");
        await Assert.That(result.CombinedOutput).Contains("err 19999");
    }

    [Test]
    [RequiresPosixShell]
    public async Task Run_PassesArgumentsVerbatim()
    {
        string[] args = ["-c", "for a in \"$@\"; do echo \"[$a]\"; done", "sh", "a b", "c\"d", "$HOME", ""];

        var result = await ProcessRunner.RunAsync("/bin/sh", args);

        await Assert.That(result.StandardOutput.Replace("\r", "")).IsEqualTo("[a b]\n[c\"d]\n[$HOME]\n[]\n");
    }

    [Test]
    [RequiresPosixShell]
    public async Task Run_Timeout_KillsProcessTree()
    {
        var stopwatch = Stopwatch.StartNew();
        // The child spawns grandchildren that would otherwise keep the pipes open, and prints their pids.
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", "sleep 60 & echo $!; sleep 60 & echo $!; wait"], timeout: TimeSpan.FromSeconds(1));
        stopwatch.Stop();

        await Assert.That(result.TimedOut).IsTrue();
        await Assert.That(result.Succeeded).IsFalse();
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(30));

        var grandchildren = result.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToList();
        await Assert.That(grandchildren.Count).IsEqualTo(2);
        foreach (var pid in grandchildren)
            await Assert.That(await IsGoneAsync(pid)).IsTrue();
    }

    [Test]
    [RequiresPosixShell]
    public async Task Run_GrandchildHoldingPipes_DoesNotKeepTheRunOpen()
    {
        // Like a build server spawned by 'dotnet build': it inherits stdout and outlives the command.
        var stopwatch = Stopwatch.StartNew();
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", "sleep 30 & echo started"], timeout: TimeSpan.FromSeconds(60));
        stopwatch.Stop();

        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.ExitCode).IsEqualTo(0);
        await Assert.That(result.StandardOutput).Contains("started");
        await Assert.That(stopwatch.Elapsed).IsLessThan(TimeSpan.FromSeconds(20));
    }

    [Test]
    [RequiresPosixShell]
    public async Task EnsureSuccess_IncludesStdoutAndStderrInMessage()
    {
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", "echo 'error NU1101: on stdout'; echo 'on stderr' >&2; exit 3"]);

        var ex = Assert.Throws<InvalidOperationException>(() => result.EnsureSuccess("Restoring thing"));

        await Assert.That(ex.Message).Contains("Restoring thing failed with exit code 3");
        await Assert.That(ex.Message).Contains("error NU1101: on stdout");
        await Assert.That(ex.Message).Contains("on stderr");
    }

    [Test]
    [RequiresPosixShell]
    public async Task Run_DoesNotWaitForStdin()
    {
        // stdin is closed immediately, so a child reading it sees EOF instead of hanging.
        var result = await ProcessRunner.RunAsync("/bin/sh", ["-c", "cat; echo done"], timeout: TimeSpan.FromSeconds(30));

        await Assert.That(result.TimedOut).IsFalse();
        await Assert.That(result.StandardOutput.Trim()).IsEqualTo("done");
    }

    [Test]
    public async Task Run_MissingExecutable_ThrowsClearError()
    {
        var ex = Assert.Throws<InvalidOperationException>(() =>
            ProcessRunner.Run("glosharp-definitely-not-a-real-program", []));
        await Assert.That(ex.Message).Contains("glosharp-definitely-not-a-real-program");
    }

    [Test]
    public async Task Run_Dotnet_Works()
    {
        var result = await ProcessRunner.RunAsync(FrameworkResolver.GetDotnetExecutable(), ["--version"]);
        await Assert.That(result.Succeeded).IsTrue();
        await Assert.That(result.StandardOutput.Trim().Length).IsGreaterThan(0);
    }

    private static async Task<bool> IsGoneAsync(int pid)
    {
        for (var i = 0; i < 20; i++)
        {
            try
            {
                using var p = Process.GetProcessById(pid);
                if (p.HasExited) return true;
            }
            catch (ArgumentException)
            {
                return true;
            }
            await Task.Delay(100);
        }
        return false;
    }
}
