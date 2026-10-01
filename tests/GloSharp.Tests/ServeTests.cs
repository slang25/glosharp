using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GloSharp.Cli;
using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary><c>glosharp serve</c>: the JSON-lines protocol the Node bridge's worker pool speaks.</summary>
public class ServeTests
{
    private const string Snippet = "var greeting = \"Hello\";\n//  ^?\nConsole.WriteLine(greeting.Length);\n";

    private sealed record ServeRun(int ExitCode, List<JsonObject> Messages, string Log)
    {
        public JsonObject Handshake => Messages[0];
        public JsonObject Response(object id) =>
            Messages.Skip(1).Single(m => m["id"]?.ToJsonString() == JsonSerializer.Serialize(id));
    }

    /// <summary>Runs the server over the given request lines until end of input.</summary>
    private static async Task<ServeRun> Serve(params string[] lines)
    {
        var input = new StringReader(string.Join("\n", lines) + "\n");
        var output = new StringWriter();
        var log = new StringWriter();
        var exit = await new ServeServer(input, output, log, concurrency: 4).RunAsync();

        var messages = output.ToString()
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => JsonNode.Parse(line)!.AsObject())
            .ToList();
        return new ServeRun(exit, messages, log.ToString());
    }

    private static string Request(object id, string command, string? code = null, object? options = null) =>
        JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["id"] = id,
            ["command"] = command,
            ["code"] = code,
            ["options"] = options,
        });

    private static async Task<(int ExitCode, string Out, string Error)> OneShot(string[] args, string? stdin = null)
    {
        var @out = new StringWriter();
        var err = new StringWriter();
        var exit = await CliApp.RunAsync(args, new CliConsole
        {
            Out = @out,
            Error = err,
            In = new StringReader(stdin ?? ""),
            IsInputRedirected = stdin != null,
        });
        return (exit, @out.ToString(), err.ToString());
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gs-serve-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------- handshake & lifecycle ----------

    [Test]
    public async Task Handshake_IsTheFirstLine_WithProtocolAndVersion()
    {
        var run = await Serve();

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Messages.Count).IsEqualTo(1);
        await Assert.That(run.Handshake["type"]!.GetValue<string>()).IsEqualTo("ready");
        await Assert.That(run.Handshake["protocol"]!.GetValue<int>()).IsEqualTo(ServeServer.ProtocolVersion);
        await Assert.That(run.Handshake["version"]!.GetValue<string>()).IsEqualTo(CliApp.Version());
        await Assert.That(run.Handshake["commands"]!.AsArray().Select(c => c!.GetValue<string>()))
            .IsEquivalentTo(new[] { "process", "render", "ping" });
    }

    [Test]
    public async Task EndOfInput_WaitsForEveryRequest_ThenExitsZero()
    {
        var run = await Serve(
            Request(1, "process", "var a = 1;"),
            Request(2, "process", "var b = 2;"),
            Request(3, "ping"));

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Messages.Count).IsEqualTo(4);
        foreach (var id in new[] { 1, 2, 3 })
            await Assert.That(run.Response(id)["ok"]!.GetValue<bool>()).IsTrue();
    }

    // ---------- results match the one-shot commands ----------

    [Test]
    public async Task Process_ResultIsTheOneShotJson()
    {
        var oneShot = await OneShot(["process", "--stdin"], Snippet);
        var run = await Serve(Request(1, "process", Snippet));

        var response = run.Response(1);
        await Assert.That(response["ok"]!.GetValue<bool>()).IsTrue();
        await Assert.That(JsonNode.DeepEquals(response["result"], JsonNode.Parse(oneShot.Out))).IsTrue();
        // Same serializer settings, just on one line.
        await Assert.That(response["result"]!.ToJsonString())
            .IsEqualTo(JsonNode.Parse(oneShot.Out)!.ToJsonString());
    }

    [Test]
    public async Task Render_ResultIsTheOneShotHtml()
    {
        var oneShot = await OneShot(["render", "--stdin", "--theme", "github-light", "--standalone"], Snippet);
        var run = await Serve(Request("r1", "render", Snippet, new { theme = "github-light", standalone = true }));

        var response = run.Response("r1");
        await Assert.That(response["ok"]!.GetValue<bool>()).IsTrue();
        await Assert.That(response["result"]!.GetValue<string>()).IsEqualTo(oneShot.Out);
    }

    [Test]
    public async Task Process_RegionAndFileOptions_MatchTheOneShotCommand()
    {
        var dir = NewTempDir();
        try
        {
            var file = Path.Combine(dir, "Example.cs");
            File.WriteAllText(file, "var hidden = 1;\n#region show\nvar shown = hidden + 1;\n#endregion\n");

            var oneShot = await OneShot(["process", file, "--region", "show"]);
            var run = await Serve(Request(1, "process", options: new { file, region = "show" }));

            await Assert.That(JsonNode.DeepEquals(run.Response(1)["result"], JsonNode.Parse(oneShot.Out))).IsTrue();
            await Assert.That(run.Response(1)["result"]!["code"]!.GetValue<string>()).DoesNotContain("var hidden");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task ConcurrentRequests_AreAnsweredById()
    {
        var requests = Enumerable.Range(1, 8)
            .Select(i => Request(i, "process", $"var value{i} = {i};\n//  ^?\n"))
            .ToArray();
        var run = await Serve(requests);

        await Assert.That(run.Messages.Count).IsEqualTo(9);
        for (var i = 1; i <= 8; i++)
        {
            var hover = run.Response(i)["result"]!["hovers"]![0]!["text"]!.GetValue<string>();
            await Assert.That(hover).IsEqualTo($"(local variable) int value{i}");
        }
    }

    // ---------- config discovery ----------

    [Test]
    public async Task Cwd_IsWhereConfigDiscoveryStartsForCode_AndRelativePathsResolve()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ConfigLoader.ConfigFileName), """{ "langVersion": "12" }""");
            File.WriteAllText(Path.Combine(dir, "Example.cs"), "var x = 1;");

            var run = await Serve(
                Request(1, "process", "var x = 1;", new { cwd = dir }),
                Request(2, "process", options: new { cwd = dir, file = "Example.cs" }),
                Request(3, "process", "var x = 1;", new { cwd = dir, config = ConfigLoader.ConfigFileName }));

            foreach (var id in new[] { 1, 2, 3 })
            {
                var response = run.Response(id);
                await Assert.That(response["ok"]!.GetValue<bool>()).IsTrue();
                await Assert.That(response["result"]!["meta"]!["langVersion"]!.GetValue<string>()).IsEqualTo("12");
            }
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task File_DiscoversConfigFromItsDirectory_LikeTheOneShotCommand()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, ConfigLoader.ConfigFileName), """{ "nullable": "disable" }""");
            var file = Path.Combine(dir, "Example.cs");
            File.WriteAllText(file, "string? s = null;");

            var oneShot = await OneShot(["process", file]);
            var run = await Serve(Request(1, "process", options: new { file }));

            await Assert.That(run.Response(1)["result"]!["meta"]!["nullable"]!.GetValue<string>()).IsEqualTo("disable");
            await Assert.That(JsonNode.DeepEquals(run.Response(1)["result"], JsonNode.Parse(oneShot.Out))).IsTrue();
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------- errors ----------

    [Test]
    public async Task Failure_CarriesTheOneShotExitCodeAndStderr()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"gs-missing-{Guid.NewGuid():N}.cs");
        var oneShot = await OneShot(["process", missing]);
        var run = await Serve(Request(1, "process", options: new { file = missing }));

        var error = run.Response(1)["error"]!;
        await Assert.That(run.Response(1)["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(error["kind"]!.GetValue<string>()).IsEqualTo("failure");
        await Assert.That(error["exitCode"]!.GetValue<int>()).IsEqualTo(oneShot.ExitCode);
        await Assert.That(error["stderr"]!.GetValue<string>()).IsEqualTo(oneShot.Error);
        await Assert.That(error["message"]!.GetValue<string>()).Contains("file not found");
        // ...and it is logged to the server's stderr, never stdout.
        await Assert.That(run.Log).Contains("file not found");
    }

    [Test]
    public async Task UsageErrors_MatchTheOneShotCommand()
    {
        var oneShot = await OneShot(["process", "--stdin", "--framework", "banana"], "var x = 1;");
        var run = await Serve(
            Request(1, "process", "var x = 1;", new { framework = "banana" }),
            Request(2, "render", "var x = 1;", new { theme = "no-such-theme" }),
            Request(3, "process", "var x = 1;", new { theme = "github-dark" }),
            Request(4, "process", "var x = 1;", new { file = "a.cs" }),
            Request(5, "process", "var x = 1;", new { bogus = true }),
            Request(6, "process", "var x = 1;", new { framework = 8 }),
            Request(7, "process"));

        var first = run.Response(1)["error"]!;
        await Assert.That(first["kind"]!.GetValue<string>()).IsEqualTo("usage");
        await Assert.That(first["exitCode"]!.GetValue<int>()).IsEqualTo(2);
        await Assert.That(first["stderr"]!.GetValue<string>()).IsEqualTo(oneShot.Error);

        await Assert.That(run.Response(2)["error"]!["message"]!.GetValue<string>()).Contains("unknown theme");
        await Assert.That(run.Response(3)["error"]!["message"]!.GetValue<string>()).Contains("unknown option '--theme'");
        await Assert.That(run.Response(4)["error"]!["message"]!.GetValue<string>()).Contains("not both");
        await Assert.That(run.Response(5)["error"]!["message"]!.GetValue<string>()).Contains("unknown option 'bogus'");
        await Assert.That(run.Response(6)["error"]!["message"]!.GetValue<string>()).Contains("must be a string");
        await Assert.That(run.Response(7)["error"]!["message"]!.GetValue<string>()).Contains("no input");
        for (var id = 2; id <= 7; id++)
            await Assert.That(run.Response(id)["error"]!["kind"]!.GetValue<string>()).IsEqualTo("usage");
    }

    [Test]
    public async Task MalformedLines_GetProtocolErrors_AndTheServerCarriesOn()
    {
        var run = await Serve(
            "this is not json",
            "[1, 2, 3]",
            """{"command":"process","code":"var x = 1;"}""",
            """{"id":true,"command":"ping"}""",
            """{"id":5,"command":"compile"}""",
            """{"id":6,"command":"process","code":42}""",
            """{"id":7,"command":"process","code":"x","options":[]}""",
            "",
            """{"id":8,"command":"ping"}""");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        var anonymous = run.Messages.Skip(1).Where(m => m["id"] is null).ToList();
        await Assert.That(anonymous.Count).IsEqualTo(4);
        foreach (var message in anonymous)
            await Assert.That(message["error"]!["kind"]!.GetValue<string>()).IsEqualTo("protocol");

        await Assert.That(run.Response(5)["error"]!["message"]!.GetValue<string>()).Contains("unknown command 'compile'");
        await Assert.That(run.Response(6)["error"]!["kind"]!.GetValue<string>()).IsEqualTo("protocol");
        await Assert.That(run.Response(7)["error"]!["kind"]!.GetValue<string>()).IsEqualTo("protocol");
        await Assert.That(run.Response(8)["result"]!.GetValue<string>()).IsEqualTo("pong");
    }

    [Test]
    public async Task InvalidConcurrency_IsUsageError()
    {
        var @out = new StringWriter();
        var err = new StringWriter();
        var exit = await CliApp.RunAsync(["serve", "--concurrency", "0"], new CliConsole
        {
            Out = @out,
            Error = err,
            In = new StringReader(""),
            IsInputRedirected = true,
        });

        await Assert.That(exit).IsEqualTo(2);
        await Assert.That(@out.ToString()).IsEmpty();
        await Assert.That(err.ToString()).Contains("--concurrency");
    }

    // ---------- the real executable ----------

    [Test]
    public async Task SpawnedCli_WritesOnlyProtocolLinesToStdout_AndExitsOnEndOfInput()
    {
        var cliDll = Path.Combine(AppContext.BaseDirectory, "GloSharp.Cli.dll");
        var start = new ProcessStartInfo(FrameworkResolver.GetDotnetExecutable())
        {
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            StandardInputEncoding = new UTF8Encoding(false),
            StandardOutputEncoding = Encoding.UTF8,
        };
        start.ArgumentList.Add(cliDll);
        start.ArgumentList.Add("serve");

        using var process = Process.Start(start)!;
        var stderrTask = process.StandardError.ReadToEndAsync();

        var handshake = JsonNode.Parse((await process.StandardOutput.ReadLineAsync())!)!;
        await Assert.That(handshake["type"]!.GetValue<string>()).IsEqualTo("ready");

        // Non-ASCII survives the round trip; a failing request logs to stderr only.
        await process.StandardInput.WriteLineAsync(Request(1, "process", "string café = \"漢字 🎉\";\n//  ^?\n"));
        await process.StandardInput.WriteLineAsync(Request(2, "process", options: new { file = "/does/not/exist.cs" }));
        await process.StandardInput.WriteLineAsync("garbage");
        process.StandardInput.Close();

        var lines = new List<JsonObject>();
        while (await process.StandardOutput.ReadLineAsync() is { } line)
            lines.Add(JsonNode.Parse(line)!.AsObject());
        await process.WaitForExitAsync();

        await Assert.That(process.ExitCode).IsEqualTo(0);
        await Assert.That(lines.Count).IsEqualTo(3);
        var ok = lines.Single(l => l["id"]?.GetValue<int>() == 1);
        var hoverTexts = ok["result"]!["hovers"]!.AsArray().Select(h => h!["text"]!.GetValue<string>());
        await Assert.That(hoverTexts).Contains("(local variable) string café");
        await Assert.That(lines.Single(l => l["id"]?.GetValue<int>() == 2)["ok"]!.GetValue<bool>()).IsFalse();
        await Assert.That(await stderrTask).Contains("file not found");
    }
}
