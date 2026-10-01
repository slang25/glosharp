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
        return RunAsync(fileName, arguments, workingDirectory, timeout, environment, cancellationToken)
            .GetAwaiter().GetResult();
    }

    public static async Task<ProcessRunResult> RunAsync(
        string fileName,
        IEnumerable<string> arguments,
        string? workingDirectory = null,
        TimeSpan? timeout = null,
        IReadOnlyDictionary<string, string?>? environment = null,
        CancellationToken cancellationToken = default)
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
        var stdoutDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var stderrDone = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

        var exited = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        process.Exited += (_, _) => exited.TrySetResult();
        process.OutputDataReceived += (_, e) =>
        {
            if (e.Data == null) { stdoutDone.TrySetResult(); return; }
            lock (gate) { stdout.AppendLine(e.Data); combined.AppendLine(e.Data); }
        };
        process.ErrorDataReceived += (_, e) =>
        {
            if (e.Data == null) { stderrDone.TrySetResult(); return; }
            lock (gate) { stderr.AppendLine(e.Data); combined.AppendLine(e.Data); }
        };

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
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        // Wait for the process itself to exit — not for its pipes to close, which
        // WaitForExitAsync also does: a grandchild that inherited them (a build server) would
        // otherwise hold the run open long after the command finished.
        var effectiveTimeout = timeout ?? DefaultTimeout;
        if (process.HasExited)
            exited.TrySetResult();
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var delay = Task.Delay(effectiveTimeout, timeoutCts.Token);
        var first = await Task.WhenAny(exited.Task, delay).ConfigureAwait(false);
        timeoutCts.Cancel();

        var timedOut = false;
        if (first != exited.Task)
        {
            KillTree(process);
            cancellationToken.ThrowIfCancellationRequested();
            timedOut = true;
        }

        // Exit has been observed (or the tree killed); wait briefly for the pipes to flush.
        await Task.WhenAny(
            Task.WhenAll(stdoutDone.Task, stderrDone.Task),
            Task.Delay(TimeSpan.FromSeconds(5))).ConfigureAwait(false);
        stopwatch.Stop();

        int exitCode;
        try { exitCode = timedOut ? -1 : process.ExitCode; }
        catch (InvalidOperationException) { exitCode = -1; }

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
                process.Kill(entireProcessTree: true);
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
}
