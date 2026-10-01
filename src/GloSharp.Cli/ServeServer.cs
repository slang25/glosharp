using System.Text;
using System.Text.Json;
using GloSharp.Core;

namespace GloSharp.Cli;

/// <summary>
/// <c>glosharp serve</c>: a long-running worker answering <c>process</c> / <c>render</c> requests
/// as JSON lines, so a docs build pays .NET startup, MEF composition and reference loading once
/// instead of once per snippet. One <see cref="GloSharpProcessor"/> (and so one compilation
/// context cache) serves every request.
/// <para>
/// Each request runs through exactly the code the one-shot command runs (<see cref="CliApp.ProcessAsync"/>
/// / <see cref="CliApp.RenderAsync"/>, with its options turned into command-line arguments and
/// parsed by the same <see cref="ArgumentParser"/>), so results, config discovery and error
/// messages match <c>glosharp process</c> / <c>glosharp render</c> exactly.
/// </para>
/// </summary>
internal sealed class ServeServer
{
    /// <summary>Bumped on incompatible protocol changes; clients refuse other versions.</summary>
    public const int ProtocolVersion = 1;

    private static readonly string[] SupportedCommands = ["process", "render", "ping"];

    /// <summary>
    /// For the envelope, HTML and messages: the stream is never embedded in a web page, so there
    /// is no need to escape &lt;, &gt;, quotes and non-ASCII (which would bloat rendered HTML).
    /// The process result itself is serialized exactly like the one-shot command's output.
    /// </summary>
    private static readonly JsonSerializerOptions EnvelopeJson = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>Request option → CLI option (null: the positional file). Booleans become flags.</summary>
    private static readonly Dictionary<string, (string? Flag, bool IsFlag, bool IsPath)> OptionMap = new(StringComparer.Ordinal)
    {
        ["file"] = (null, false, true),
        ["framework"] = ("--framework", false, false),
        ["project"] = ("--project", false, true),
        ["complog"] = ("--complog", false, true),
        ["complogProject"] = ("--complog-project", false, false),
        ["region"] = ("--region", false, false),
        ["noRestore"] = ("--no-restore", true, false),
        ["cacheDir"] = ("--cache-dir", false, true),
        ["config"] = ("--config", false, true),
        ["theme"] = ("--theme", false, false),
        ["standalone"] = ("--standalone", true, false),
    };

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly TextWriter _log;
    private readonly int _concurrency;
    private readonly SemaphoreSlim _slots;
    private readonly GloSharpProcessor _processor = new();
    private readonly object _writeLock = new();
    private readonly HashSet<Task> _inflight = [];
    private bool _outputBroken;

    public ServeServer(TextReader input, TextWriter output, TextWriter log, int concurrency)
    {
        _input = input;
        _output = output;
        _log = TextWriter.Synchronized(log);
        _concurrency = Math.Max(1, concurrency);
        _slots = new SemaphoreSlim(_concurrency);
    }

    public static async Task<int> RunAsync(ParsedCommand parsed, CliConsole console)
    {
        var concurrency = Environment.ProcessorCount;
        if (parsed.Get("--concurrency") is { } text && (!int.TryParse(text, out concurrency) || concurrency < 1))
            throw new UsageException($"invalid --concurrency value: '{text}' (expected a positive integer).");

        var input = console.In;
        var output = console.Out;
        if (ReferenceEquals(output, Console.Out))
        {
            // Real stdio: talk to the raw streams, and point Console.Out at stderr so nothing else
            // (a library, a stray debug line) can corrupt the protocol stream.
            output = new StreamWriter(Console.OpenStandardOutput(), new UTF8Encoding(false)) { AutoFlush = false };
            Console.SetOut(console.Error);
        }
        if (ReferenceEquals(input, Console.In))
            input = new StreamReader(Console.OpenStandardInput(), new UTF8Encoding(false));

        return await new ServeServer(input, output, console.Error, concurrency).RunAsync();
    }

    /// <summary>Serves until end of input, then waits for every request to be answered.</summary>
    public async Task<int> RunAsync()
    {
        Write(JsonSerializer.Serialize(new
        {
            type = "ready",
            protocol = ProtocolVersion,
            version = CliApp.Version(),
            commands = SupportedCommands,
            concurrency = _concurrency,
            pid = Environment.ProcessId,
        }, EnvelopeJson));

        while (true)
        {
            string? line;
            try
            {
                line = await _input.ReadLineAsync();
            }
            catch (IOException)
            {
                break;
            }

            if (line == null)
                break;
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var task = Task.Run(() => HandleLineAsync(line));
            lock (_inflight)
                _inflight.Add(task);
            _ = task.ContinueWith(t =>
            {
                lock (_inflight)
                    _inflight.Remove(t);
            }, TaskScheduler.Default);
        }

        Task[] remaining;
        lock (_inflight)
            remaining = [.. _inflight];
        await Task.WhenAll(remaining);
        return _outputBroken ? ExitCodes.Failure : ExitCodes.Success;
    }

    private sealed class ProtocolException(string message) : Exception(message);

    private sealed record Request(string Id, string Command, string? Code, JsonElement? Options);

    private async Task HandleLineAsync(string line)
    {
        string? id = null;
        try
        {
            var request = Parse(line, ref id);
            if (request.Command == "ping")
            {
                WriteResult(request.Id, "\"pong\"");
                return;
            }

            await _slots.WaitAsync();
            try
            {
                await ExecuteAsync(request);
            }
            finally
            {
                _slots.Release();
            }
        }
        catch (ProtocolException ex)
        {
            WriteError(id, "protocol", ex.Message, ExitCodes.Usage, "");
        }
        catch (Exception ex)
        {
            // Never take the worker down for one bad request.
            _log.WriteLine($"glosharp serve: internal error: {ex}");
            WriteError(id, "failure", ex.Message, ExitCodes.Failure, "");
        }
    }

    private static Request Parse(string line, ref string? id)
    {
        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(line);
        }
        catch (JsonException ex)
        {
            throw new ProtocolException($"malformed request (not JSON): {ex.Message}");
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
                throw new ProtocolException("malformed request: expected a JSON object.");

            if (!root.TryGetProperty("id", out var idElement) ||
                idElement.ValueKind is not (JsonValueKind.Number or JsonValueKind.String))
                throw new ProtocolException("malformed request: \"id\" (a number or string) is required.");
            id = idElement.GetRawText();

            if (!root.TryGetProperty("command", out var commandElement) || commandElement.ValueKind != JsonValueKind.String)
                throw new ProtocolException("malformed request: \"command\" (a string) is required.");
            var command = commandElement.GetString()!;
            if (!SupportedCommands.Contains(command))
                throw new ProtocolException(
                    $"unknown command '{command}'. Supported: {string.Join(", ", SupportedCommands)}.");

            string? code = null;
            if (root.TryGetProperty("code", out var codeElement) && codeElement.ValueKind != JsonValueKind.Null)
            {
                if (codeElement.ValueKind != JsonValueKind.String)
                    throw new ProtocolException("malformed request: \"code\" must be a string.");
                code = codeElement.GetString();
            }

            JsonElement? options = null;
            if (root.TryGetProperty("options", out var optionsElement) && optionsElement.ValueKind != JsonValueKind.Null)
            {
                if (optionsElement.ValueKind != JsonValueKind.Object)
                    throw new ProtocolException("malformed request: \"options\" must be an object.");
                options = optionsElement.Clone();
            }

            return new Request(id, command, code, options);
        }
    }

    private async Task ExecuteAsync(Request request)
    {
        var spec = request.Command == "render" ? CliApp.RenderSpec : CliApp.ProcessSpec;
        var stderr = new StringWriter();
        var console = new CliConsole
        {
            Out = TextWriter.Null,
            Error = stderr,
            In = new StringReader(request.Code ?? ""),
            IsInputRedirected = request.Code != null,
        };

        try
        {
            var (args, workingDirectory) = BuildArguments(request);
            var parsed = ArgumentParser.Parse(spec, args);
            if (request.Command == "render")
            {
                var html = await CliApp.RenderAsync(parsed, console, _processor, workingDirectory);
                WriteResult(request.Id, JsonSerializer.Serialize(html, EnvelopeJson));
            }
            else
            {
                var result = await CliApp.ProcessAsync(parsed, console, _processor, workingDirectory);
                WriteResult(request.Id, JsonOutput.SerializeCompact(result));
            }
        }
        catch (UsageException ex)
        {
            CliApp.UsageError(console, spec, ex.Message);
            WriteError(request.Id, "usage", ex.Message, ExitCodes.Usage, stderr.ToString());
        }
        catch (Exception ex)
        {
            CliApp.Fail(console, spec.Name, ex);
            WriteError(request.Id, "failure", ex.Message, ExitCodes.Failure, stderr.ToString());
        }
        finally
        {
            // What the one-shot command would have printed (restore progress, errors) goes to our
            // stderr, as a block per request.
            var text = stderr.ToString();
            if (text.Length > 0)
                _log.Write(text);
        }
    }

    /// <summary>
    /// Turns the request's options into the arguments the one-shot command would get. With a
    /// <c>cwd</c> other than ours, relative paths are resolved against it and config discovery
    /// for stdin input starts there.
    /// </summary>
    private static (List<string> Args, string? WorkingDirectory) BuildArguments(Request request)
    {
        var options = request.Options;
        string? workingDirectory = null;
        if (options is { } o && o.TryGetProperty("cwd", out var cwdElement) && cwdElement.ValueKind != JsonValueKind.Null)
        {
            if (cwdElement.ValueKind != JsonValueKind.String || cwdElement.GetString() is not { Length: > 0 } cwd)
                throw new UsageException("option 'cwd' must be a non-empty string.");
            var full = Path.GetFullPath(cwd);
            if (!Directory.Exists(full))
                throw new UsageException($"option 'cwd': directory not found: {cwd}");
            if (!SamePath(full, Directory.GetCurrentDirectory()))
                workingDirectory = full;
        }

        var args = new List<string>();
        string? file = null;
        if (options is { } opts)
        {
            foreach (var property in opts.EnumerateObject())
            {
                if (property.Name == "cwd")
                    continue;
                if (!OptionMap.TryGetValue(property.Name, out var mapping))
                    throw new UsageException($"unknown option '{property.Name}'.");

                var value = property.Value;
                if (value.ValueKind == JsonValueKind.Null)
                    continue;

                if (mapping.IsFlag)
                {
                    if (value.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
                        throw new UsageException($"option '{property.Name}' must be true or false.");
                    if (value.ValueKind == JsonValueKind.True)
                        args.Add(mapping.Flag!);
                    continue;
                }

                if (value.ValueKind != JsonValueKind.String)
                    throw new UsageException($"option '{property.Name}' must be a string.");
                var text = value.GetString()!;
                if (mapping.IsPath && workingDirectory != null && text.Length > 0)
                    text = Path.GetFullPath(text, workingDirectory);

                if (mapping.Flag == null)
                {
                    file = text;
                }
                else if (text.StartsWith('-'))
                {
                    args.Add($"{mapping.Flag}={text}"); // so the value isn't read as an option
                }
                else
                {
                    args.Add(mapping.Flag);
                    args.Add(text);
                }
            }
        }

        if (request.Code == null && file == null)
            throw new UsageException("no input: pass \"code\", or a file path in options.file.");
        if (request.Code != null)
            args.Add("--stdin");
        if (file != null)
        {
            args.Add("--");
            args.Add(file);
        }

        return (args, workingDirectory);
    }

    private static bool SamePath(string a, string b)
    {
        var comparison = OperatingSystem.IsWindows() || OperatingSystem.IsMacOS()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        return string.Equals(Path.TrimEndingDirectorySeparator(a), Path.TrimEndingDirectorySeparator(b), comparison);
    }

    // ---------------------------------------------------------------------------------
    // Output
    // ---------------------------------------------------------------------------------

    private void WriteResult(string id, string resultJson) =>
        Write($"{{\"id\":{id},\"ok\":true,\"result\":{resultJson}}}");

    private void WriteError(string? id, string kind, string message, int exitCode, string stderr)
    {
        var error = JsonSerializer.Serialize(new { kind, message, exitCode, stderr }, EnvelopeJson);
        Write($"{{\"id\":{id ?? "null"},\"ok\":false,\"error\":{error}}}");
    }

    private void Write(string line)
    {
        lock (_writeLock)
        {
            if (_outputBroken)
                return;
            try
            {
                _output.Write(line);
                _output.Write('\n');
                _output.Flush();
            }
            catch (Exception ex) when (ex is IOException or ObjectDisposedException)
            {
                // The client went away; finish quietly once input ends.
                _outputBroken = true;
            }
        }
    }
}
