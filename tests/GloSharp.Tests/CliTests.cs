using System.Text.Json;
using System.Text.RegularExpressions;
using GloSharp.Cli;
using GloSharp.Core;

namespace GloSharp.Tests;

// R-core #17, U-cli F15/F16/F26, U-proj F4/F13/F21/F23/F24, R-infra #36.
public class CliTests
{
    private sealed class ThrowingReader : TextReader
    {
        public override string ReadToEnd() => throw new InvalidOperationException("stdin must not be read");
        public override Task<string> ReadToEndAsync() => throw new InvalidOperationException("stdin must not be read");
        public override int Read() => throw new InvalidOperationException("stdin must not be read");
    }

    private sealed record CliRun(int ExitCode, string Out, string Error);

    private static async Task<CliRun> Run(string[] args, string? stdin = null, bool redirected = false)
    {
        var @out = new StringWriter();
        var err = new StringWriter();
        var console = new CliConsole
        {
            Out = @out,
            Error = err,
            In = stdin != null ? new StringReader(stdin) : new ThrowingReader(),
            IsInputRedirected = redirected || stdin != null,
        };
        var exit = await CliApp.RunAsync(args, console);
        return new CliRun(exit, @out.ToString(), err.ToString());
    }

    private static string NewTempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), $"gs-cli-{Guid.NewGuid():N}");
        Directory.CreateDirectory(dir);
        return dir;
    }

    // ---------- parser ----------

    [Test]
    public async Task Parser_UnknownOption_SuggestsClosest()
    {
        var ex = Assert.Throws<UsageException>(() =>
            ArgumentParser.Parse(CliApp.ProcessSpec, ["a.cs", "--framwork", "net9.0"]));
        await Assert.That(ex.Message).Contains("unknown option '--framwork'");
        await Assert.That(ex.Message).Contains("Did you mean '--framework'?");
    }

    [Test]
    public async Task Parser_MissingValue_IsAnError_EvenBeforeAnotherOption()
    {
        var atEnd = Assert.Throws<UsageException>(() => ArgumentParser.Parse(CliApp.ProcessSpec, ["a.cs", "--framework"]));
        var beforeOption = Assert.Throws<UsageException>(() => ArgumentParser.Parse(CliApp.ProcessSpec, ["--framework", "--stdin"]));

        await Assert.That(atEnd.Message).Contains("requires a value");
        await Assert.That(beforeOption.Message).Contains("requires a value");
    }

    [Test]
    public async Task Parser_OptionAndValueInOneArgument_ExplainsTheProblem()
    {
        var ex = Assert.Throws<UsageException>(() => ArgumentParser.Parse(CliApp.ProcessSpec, ["--framework net8.0", "a.cs"]));
        await Assert.That(ex.Message).Contains("single argument");
    }

    [Test]
    public async Task Parser_SupportsEqualsSyntax_AliasesAndTerminator()
    {
        var parsed = ArgumentParser.Parse(CliApp.RenderSpec, ["--framework=net8.0", "-o", "out.html", "--", "-weird.cs"]);

        await Assert.That(parsed.Get("--framework")).IsEqualTo("net8.0");
        await Assert.That(parsed.Get("--output")).IsEqualTo("out.html");
        await Assert.That(parsed.Positionals).IsEquivalentTo(new[] { "-weird.cs" });
    }

    [Test]
    public async Task Parser_SurplusPositional_IsAnError()
    {
        var ex = Assert.Throws<UsageException>(() => ArgumentParser.Parse(CliApp.ProcessSpec, ["a.cs", "b.cs"]));
        await Assert.That(ex.Message).Contains("unexpected argument 'b.cs'");
    }

    [Test]
    public async Task Parser_HelpWinsOverErrors()
    {
        var parsed = ArgumentParser.Parse(CliApp.VerifySpec, ["--bogus", "--help"]);
        await Assert.That(parsed.HelpRequested).IsTrue();
    }

    // ---------- global ----------

    [Test]
    public async Task Version_PrintsToStdout()
    {
        var run = await Run(["--version"]);
        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Out.Trim()).StartsWith("0.");
    }

    [Test]
    public async Task Help_GoesToStdout_AndListsCommands()
    {
        var run = await Run(["--help"]);
        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Error).IsEmpty();
        await Assert.That(run.Out).Contains("compact-complog");
        await Assert.That(run.Out).Contains("--version");
    }

    [Test]
    public async Task UnknownCommand_IsUsageError()
    {
        var run = await Run(["proces"]);
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("unknown command 'proces'");
    }

    // ---------- per-command help never reads stdin or writes files (U-proj F13) ----------

    [Test]
    [Arguments("process")]
    [Arguments("render")]
    [Arguments("verify")]
    [Arguments("init")]
    [Arguments("compact-complog")]
    public async Task CommandHelp_PrintsUsage_WithoutSideEffects(string command)
    {
        var before = File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ConfigLoader.ConfigFileName));

        var run = await Run([command, "--help"]);

        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Out).StartsWith($"Usage: glosharp {command}");
        await Assert.That(File.Exists(Path.Combine(Directory.GetCurrentDirectory(), ConfigLoader.ConfigFileName))).IsEqualTo(before);
    }

    // ---------- input handling ----------

    [Test]
    public async Task Process_NoInputAndInteractiveStdin_FailsInsteadOfHanging()
    {
        var run = await Run(["process"]);
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("--stdin");
    }

    [Test]
    public async Task Process_RegionWithStdin_IsAllowed()
    {
        var run = await Run(["process", "--stdin", "--region", "show"],
            stdin: "var hidden = 1;\n#region show\nvar shown = 2;\n#endregion\n");

        await Assert.That(run.ExitCode).IsEqualTo(0);
        using var json = JsonDocument.Parse(run.Out);
        var code = json.RootElement.GetProperty("code").GetString()!;
        await Assert.That(code).Contains("shown");
        await Assert.That(code).DoesNotContain("hidden");
    }

    [Test]
    public async Task Process_RedirectedStdinWithoutFlag_IsRead()
    {
        var run = await Run(["process"], stdin: "var x = 1;");
        await Assert.That(run.ExitCode).IsEqualTo(0);
        await Assert.That(run.Out).Contains("\"code\"");
    }

    [Test]
    public async Task Process_FileAndStdin_IsUsageError()
    {
        var run = await Run(["process", "a.cs", "--stdin"], stdin: "");
        await Assert.That(run.ExitCode).IsEqualTo(2);
    }

    [Test]
    public async Task Process_InvalidFramework_IsUsageError()
    {
        var run = await Run(["process", "--stdin", "--framework", "banana"], stdin: "var x = 1;");
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("not a valid target framework");
    }

    [Test]
    public async Task Process_ComplogAndProject_IsUsageError()
    {
        var run = await Run(["process", "--stdin", "--complog", "a.complog", "--project", "b.csproj"], stdin: "");
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("mutually exclusive");
    }

    [Test]
    public async Task Process_SolutionAsProject_FailsBeforeRestoring()
    {
        var dir = NewTempDir();
        try
        {
            var sln = Path.Combine(dir, "All.slnx");
            File.WriteAllText(sln, "<Solution />");

            var run = await Run(["process", "--stdin", "--project", sln], stdin: "var x = 1;");

            await Assert.That(run.ExitCode).IsEqualTo(1);
            await Assert.That(run.Error).Contains("is a solution");
            await Assert.That(run.Error).DoesNotContain("Running dotnet restore");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Process_RestoreFailure_ShowsNuGetErrorFromStdout()
    {
        var dir = NewTempDir();
        try
        {
            var csproj = Path.Combine(dir, "Bad.csproj");
            File.WriteAllText(csproj, """
                <Project Sdk="Microsoft.NET.Sdk">
                  <PropertyGroup><TargetFramework>net8.0</TargetFramework></PropertyGroup>
                  <ItemGroup><PackageReference Include="This.Package.Does.Not.Exist.GloSharpCli" Version="1.0.0" /></ItemGroup>
                </Project>
                """);

            var run = await Run(["process", "--stdin", "--project", csproj], stdin: "var x = 1;");

            await Assert.That(run.ExitCode).IsEqualTo(1);
            await Assert.That(run.Error).Contains("NU1101");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    // ---------- compact-complog (U-proj F23) ----------

    [Test]
    public async Task CompactComplog_DashOWithoutValue_IsUsageError_NotACrash()
    {
        var run = await Run(["compact-complog", "in.complog", "-o"]);
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("requires a value");
    }

    [Test]
    public async Task CompactComplog_MissingOutput_IsUsageError()
    {
        var run = await Run(["compact-complog", "in.complog"]);
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("--output");
    }

    // ---------- verify (U-proj F4, U-cli F16, R-core #17) ----------

    [Test]
    public async Task Verify_MultiplePaths_ExcludesBinObj_AndFailsOnAnyBrokenFile()
    {
        var dir = NewTempDir();
        try
        {
            Directory.CreateDirectory(Path.Combine(dir, "a", "obj"));
            Directory.CreateDirectory(Path.Combine(dir, "a", "bin", "Debug"));
            Directory.CreateDirectory(Path.Combine(dir, "b"));
            File.WriteAllText(Path.Combine(dir, "a", "good.cs"), "var x = 1;\nConsole.WriteLine(x);\n");
            File.WriteAllText(Path.Combine(dir, "a", "obj", "generated.cs"), "this does not compile");
            File.WriteAllText(Path.Combine(dir, "a", "bin", "Debug", "junk.cs"), "neither does this");
            var broken = Path.Combine(dir, "b", "broken.cs");
            File.WriteAllText(broken, "int x = \"nope\";\n");

            var onlyA = await Run(["verify", Path.Combine(dir, "a")]);
            var both = await Run(["verify", Path.Combine(dir, "a"), Path.Combine(dir, "b")]);

            await Assert.That(onlyA.ExitCode).IsEqualTo(0);
            await Assert.That(onlyA.Out).Contains("All 1 file(s) verified");
            await Assert.That(both.ExitCode).IsEqualTo(1);
            await Assert.That(both.Out).Contains($"{broken}(1,9): error CS0029:");
            await Assert.That(both.Out).Contains("1 of 2 file(s) failed");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Verify_SingleFile_IsAccepted()
    {
        var dir = NewTempDir();
        try
        {
            var file = Path.Combine(dir, "one.cs");
            File.WriteAllText(file, "var x = 1;\nConsole.WriteLine(x);\n");

            var run = await Run(["verify", file]);

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Out).Contains("All 1 file(s) verified");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Verify_EmptyDirectory_FailsInsteadOfPassing()
    {
        var dir = NewTempDir();
        try
        {
            var run = await Run(["verify", dir]);
            await Assert.That(run.ExitCode).IsEqualTo(1);
            await Assert.That(run.Error).Contains("no .cs files found");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    public async Task Verify_MissingPath_IsUsageError()
    {
        var run = await Run(["verify", "/definitely/not/a/dir"]);
        await Assert.That(run.ExitCode).IsEqualTo(2);
        await Assert.That(run.Error).Contains("path not found");
    }

    [Test]
    public async Task Verify_Region_MatchesExactName()
    {
        var dir = NewTempDir();
        try
        {
            File.WriteAllText(Path.Combine(dir, "x.cs"), "#region X\nvar a = 1;\n#endregion\n");
            File.WriteAllText(Path.Combine(dir, "xy.cs"), "#region XY\nvar b = 2;\n#endregion\n");

            var run = await Run(["verify", dir, "--region", "X"]);

            await Assert.That(run.ExitCode).IsEqualTo(0);
            await Assert.That(run.Out).Contains("All 1 file(s) verified successfully, 1 skipped");
        }
        finally { Directory.Delete(dir, recursive: true); }
    }

    [Test]
    [Arguments("#region X", "X", true)]
    [Arguments("  #region X  ", "X", true)]
    [Arguments("#region X\r", "X", true)]
    [Arguments("#region XY", "X", false)]
    [Arguments("// #region X", "X", false)]
    [Arguments("#region add-item", "add-item", true)]
    public async Task ContainsRegion_IsExact(string line, string name, bool expected)
    {
        await Assert.That(VerifyInputs.ContainsRegion($"var a = 1;\n{line}\nvar b = 2;\n", name)).IsEqualTo(expected);
    }

    // ---------- diagnostic format (U-proj F21) ----------

    [Test]
    public async Task DiagnosticFormatter_MatchesSetupDotnetProblemMatcher()
    {
        var line = DiagnosticFormatter.Format("docs/intro.cs", new GloSharpError
        {
            Line = 2,
            Character = 8,
            Length = 3,
            Code = "CS0029",
            Message = "Cannot implicitly convert type 'string' to 'int'",
            Severity = "error",
            Expected = false,
        });

        // The csc matcher registered by actions/setup-dotnet.
        var matcher = new Regex(@"^([^\s].*)\((\d+)(?:,\d+|,\d+,\d+)?\):\s+(error|warning)\s+([a-zA-Z]+(?<!MSB)\d+):\s*(.*?)\s+\[(.*?)\]$");
        var match = matcher.Match(line);

        await Assert.That(match.Success).IsTrue();
        await Assert.That(match.Groups[1].Value).IsEqualTo(Path.GetFullPath("docs/intro.cs"));
        await Assert.That(match.Groups[2].Value).IsEqualTo("3");
        await Assert.That(match.Groups[4].Value).IsEqualTo("CS0029");
        await Assert.That(match.Groups[5].Value).IsEqualTo("Cannot implicitly convert type 'string' to 'int'");
    }

    [Test]
    public async Task DiagnosticFormatter_FlattensMultilineMessages()
    {
        var line = DiagnosticFormatter.FormatFileFailure("a.cs", "first line\nsecond line");
        await Assert.That(line).DoesNotContain("\n");
        await Assert.That(line).Contains("error GS1001: first line second line");
    }
}
