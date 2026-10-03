using System.ComponentModel;
using System.Diagnostics;
using System.Text;

namespace GloSharp.Core;

/// <summary>Outcome of a child process run by <see cref="ProcessRunner"/>.</summary>
public sealed record ProcessRunResult
{
    public required string FileName { get; init; }
    public required IReadOnlyList<string> Arguments { get; init; }
    public required int ExitCode { get; init; }
    public required string StandardOutput { get; init; }
    public required string StandardError { get; init; }

    /// <summary>stdout and stderr interleaved in the order lines arrived.</summary>
    public required string CombinedOutput { get; init; }

    public required bool TimedOut { get; init; }
    public required TimeSpan Elapsed { get; init; }

    public bool Succeeded => !TimedOut && ExitCode == 0;

    /// <summary>The command line, quoted for display in error messages.</summary>
    public string CommandLine => ProcessRunner.FormatCommandLine(FileName, Arguments);

    /// <summary>
    /// Throws an <see cref="InvalidOperationException"/> carrying the command line, the exit
    /// code (or timeout) and the full captured output when the process did not succeed.
    /// MSBuild and NuGet write their errors to stdout, so stdout is always included.
    /// </summary>
    public ProcessRunResult EnsureSuccess(string description)
    {
        if (Succeeded)
            return this;

        var outcome = TimedOut
            ? $"timed out after {Elapsed.TotalSeconds:0}s and was killed"
            : $"failed with exit code {ExitCode}";
        var output = CombinedOutput.Trim();
        throw new InvalidOperationException(
            $"{description} {outcome}.\nCommand: {CommandLine}" +
            (output.Length > 0 ? $"\nOutput:\n{output}" : "\n(no output)"));
    }
}

/// <summary>
/// Runs child processes safely: arguments go through <see cref="ProcessStartInfo.ArgumentList"/>
/// (no quoting bugs), stdout and stderr are drained concurrently (no pipe-buffer deadlocks),
/// and every run has a timeout after which the whole process tree is killed.
/// </summary>
public static class ProcessRunner
{
    /// <summary>How long to keep reading output after the process exits before giving up.</summary>
    private static readonly TimeSpan PipeDrainGrace = TimeSpan.FromSeconds(5);

    /// <summary>Environment variable overriding <see cref="DefaultTimeout"/>, in seconds.</summary>
    public const string TimeoutEnvironmentVariable = "GLOSHARP_PROCESS_TIMEOUT";

    /// <summary>
    /// Default timeout for child processes (restore, property queries): 5 minutes, or the
    /// value of <c>GLOSHARP_PROCESS_TIMEOUT</c> (seconds) when set to a positive integer.
    /// </summary>
    public static TimeSpan DefaultTimeout
    {
        get
        {
            var env = Environment.GetEnvironmentVariable(TimeoutEnvironmentVariable);
            return int.TryParse(env, out var seconds) && seconds > 0
                ? TimeSpan.FromSeconds(seconds)
                : TimeSpan.FromMinutes(5);
        }
    }

    public static ProcessRunResult Run(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
    {
        // Truly synchronous: blocking on the async path from a thread-pool thread needs another
        // pool thread to complete it, and many concurrent callers (parallel tests, `glosharp
        // serve` resolving several snippets) starve the pool and stall for minutes. In
        // synchronous mode RunCoreAsync never awaits anything, so it completes inline.
        return RunCoreAsync(fileName, arguments, workingDirectory, timeout, environment, cancellationToken, synchronous: true)
            .GetAwaiter().GetResult();
    }

    public static Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default) =>
        RunCoreAsync(fileName, arguments, workingDirectory, timeout, environment, cancellationToken, synchronous: false);

    private static async Task<ProcessRunResult> RunCoreAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory,
        TimeSpan? timeout,
        IReadOnlyDictionary<string, string?>? environment,
        CancellationToken cancellationToken,
        bool synchronous)
    {
        var args = arguments.ToList();
        var psi = new ProcessStartInfo(fileName)
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        foreach (var arg in args)
            psi.ArgumentList.Add(arg);
        if (workingDirectory != null)
            psi.WorkingDirectory = workingDirectory;

        // Keep the dotnet CLI quiet and non-interactive: no first-run banner, no terminal
        // logger, no telemetry notice mixed into output we parse.
        psi.Environment["DOTNET_NOLOGO"] = "1";
        psi.Environment["DOTNET_SKIP_FIRST_TIME_EXPERIENCE"] = "1";
        psi.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        psi.Environment["MSBUILDTERMINALLOGGER"] = "off";
        // Reused MSBuild nodes outlive the command and inherit its pipes, which would keep
        // stdout open (and the run "unfinished") long after dotnet itself has exited.
        psi.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        if (environment != null)
        {
            foreach (var (key, value) in environment)
            {
                if (value == null) psi.Environment.Remove(key);
                else psi.Environment[key] = value;
            }
        }

        var stdout = new StringBuilder();
        var stderr = new StringBuilder();
        var combined = new StringBuilder();
        var gate = new object();

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) => exited.TrySetResult();

        var stopwatch = Stopwatch.StartNew();
        try
        {
            process.Start();
        }
        catch (Win32Exception ex)
        {
            throw new InvalidOperationException(
                $"Could not start '{fileName}': {ex.Message}. Ensure it is installed and on the PATH.", ex);
        }

        // Nothing is ever written to the child's stdin; closing it makes tools that would
        // otherwise wait for interactive input (credential prompts) fail fast instead.
        process.StandardInput.Close();

        // Drain both pipes on dedicated threads rather than the thread pool: callers often block
        // on Run() from pool threads, and under load pool-based readers can lag the exit by
        // seconds, which would truncate output (e.g. an empty `dotnet --version`).
        var stdoutDone = StartReader(process.StandardOutput, stdout);
        var stderrDone = StartReader(process.StandardError, stderr);

        // Wait for the process itself to exit — not for its pipes to close, which
        // WaitForExitAsync also does: a grandchild that inherited them (a build server) would
        // otherwise hold the run open long after the command finished.
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (process.HasExited)
            exited.TrySetResult();

        bool exitedInTime;
        if (synchronous)
        {
            // Blocks this thread only; a cancellation kills the tree, which ends the wait.
            using var registration = cancellationToken.Register(() => KillTree(process));
            exitedInTime = process.WaitForExit(effectiveTimeout) && !cancellationToken.IsCancellationRequested;
        }
        else
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var delay = Task.Delay(effectiveTimeout, timeoutCts.Token);
            exitedInTime = await Task.WhenAny(exited.Task, delay).ConfigureAwait(false) == exited.Task;
            timeoutCts.Cancel();
        }

        var timedOut = false;
        if (!exitedInTime)
        {
            KillTree(process);
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        // Exit has been observed (or the tree killed); wait briefly for the pipes to flush. The
        // bound only guards against a grandchild (a build server) holding the pipes open.
        // The readers run on dedicated threads, so a blocking wait needs no pool thread.
        if (synchronous)
            Task.WaitAll([stdoutDone, stderrDone], PipeDrainGrace);
        else
            await Task.WhenAny(
                Task.WhenAll(stdoutDone, stderrDone),
                Task.Delay(PipeDrainGrace)).ConfigureAwait(false);
        stopwatch.Stop();

        int exitCode;
        try { exitCode = timedOut ? -1 : process.ExitCode; }
        catch (InvalidOperationException) { exitCode = -1; }

        Task StartReader(StreamReader reader, StringBuilder target) =>
            Task.Factory.StartNew(() =>
            {
                try
                {
                    while (reader.ReadLine() is { } line)
                        lock (gate) { target.AppendLine(line); combined.AppendLine(line); }
                }
                catch (Exception ex) when (ex is IOException or ObjectDisposedException)
                {
                    // The process was killed or disposed mid-read; keep what was captured.
                }
            }, CancellationToken.None, TaskCreationOptions.LongRunning, TaskScheduler.Default);

        lock (gate)
        {
            return new ProcessRunResult
            {
                FileName = fileName,
                Arguments = args,
                ExitCode = exitCode,
                StandardOutput = stdout.ToString(),
                StandardError = stderr.ToString(),
                CombinedOutput = combined.ToString(),
                TimedOut = timedOut,
                Elapsed = stopwatch.Elapsed,
            };
        }
    }

    internal static string FormatCommandLine(string fileName, IEnumerable<string> arguments)
    {
        return string.Join(' ', new[] { fileName }.Concat(arguments).Select(Quote));

        static string Quote(string s) =>
            s.Length > 0 && s.All(c => !char.IsWhiteSpace(c) && c != '"' && c != '\'')
                ? s
                : "\"" + s.Replace("\"", "\\\"") + "\"";
    }

    private static void KillTree(Process process)
    {
        try
        {
            if (!process.HasExited)
            {
                // On macOS, Process.Kill(entireProcessTree: true) can hang the whole machine: on
                // GitHub's macOS runners it froze the VM (root processes included) within seconds,
                // every time, while killing the same trees from a ps snapshot never did.
                // https://github.com/dotnet/runtime/issues/131944
                if (OperatingSystem.IsMacOS())
                    KillTreeFromSnapshot(process);
                else
                    process.Kill(entireProcessTree: true);
            }
        }
        catch (InvalidOperationException)
        {
            // Already exited.
        }
        catch (Win32Exception)
        {
            // Best effort: the process may be exiting concurrently.
        }

        try { process.WaitForExit(5000); } catch (InvalidOperationException) { }
    }

    /// <summary>
    /// Kills <paramref name="process"/> and every descendant found in one <c>ps</c> snapshot
    /// taken before the kill (orphans get reparented, so they can't be found afterwards).
    /// </summary>
    private static void KillTreeFromSnapshot(Process process)
    {
        var descendants = Descendants(process.Id);
        process.Kill();
        foreach (var pid in descendants)
        {
            try
            {
                using var child = Process.GetProcessById(pid);
                child.Kill();
            }
            catch (Exception e) when (e is ArgumentException or InvalidOperationException or Win32Exception)
            {
                // Already exited.
            }
        }
    }

    private static List<int> Descendants(int root)
    {
        var children = new Dictionary<int, List<int>>();
        try
        {
            var psi = new ProcessStartInfo("ps", ["-A", "-o", "pid=,ppid="])
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
            };
            using var ps = Process.Start(psi)!;
            var output = ps.StandardOutput.ReadToEnd();
            ps.WaitForExit(5000);
            foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 2 && int.TryParse(parts[0], out var pid) && int.TryParse(parts[1], out var ppid))
                {
                    if (!children.TryGetValue(ppid, out var list))
                        children[ppid] = list = [];
                    list.Add(pid);
                }
            }
        }
        catch (Exception e) when (e is Win32Exception or InvalidOperationException)
        {
            // No ps: kill the process itself only.
        }

        var result = new List<int>();
        var queue = new Queue<int>([root]);
        while (queue.Count > 0)
        {
            if (!children.TryGetValue(queue.Dequeue(), out var kids))
                continue;
            foreach (var kid in kids)
            {
                result.Add(kid);
                queue.Enqueue(kid);
            }
        }
        return result;
    }
}
