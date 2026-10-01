using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using GloSharp.Core;

namespace GloSharp.Cli;

/// <summary>Console streams for the CLI (injectable so commands can be tested in-process).</summary>
internal sealed class CliConsole
{
    public required TextWriter Out { get; init; }
    public required TextWriter Error { get; init; }
    public required TextReader In { get; init; }
    public required bool IsInputRedirected { get; init; }

    public static CliConsole System => new()
    {
        Out = Console.Out,
        Error = Console.Error,
        In = Console.In,
        IsInputRedirected = Console.IsInputRedirected,
    };
}

/// <summary>
/// Exit codes: 0 success; 1 failure (processing error, failed verification, I/O);
/// 2 usage error (unknown option, missing value, bad arguments). compact-complog keeps its
/// documented codes 3–6 for specific failures.
/// </summary>
internal static class ExitCodes
{
    public const int Success = 0;
    public const int Failure = 1;
    public const int Usage = 2;
}

internal static class CliApp
{
    // ---------------------------------------------------------------------------------
    // Command specs
    // ---------------------------------------------------------------------------------

    private static readonly OptionSpec[] CompileOptions =
    [
        new("--framework", "Target framework, e.g. net8.0 (default: " + FrameworkResolver.DefaultTargetFramework + ", or the project's)", "tfm"),
        new("--project", "Project (.csproj or its directory) whose packages, project references and own build output snippets compile against", "path"),
        new("--complog", "Compiler log (.complog / .glocontext) to take references and options from", "path"),
        new("--complog-project", "Compilation to use from the complog: Name, Name.csproj or \"Name (tfm)\"", "name"),
        new("--region", "Only process the named #region of the source", "name"),
        new("--no-restore", "Don't run 'dotnet restore' automatically"),
        new("--cache-dir", "Directory for disk-based result caching", "path"),
        new("--config", "Path to glosharp.config.json (default: search upwards from the input)", "path"),
    ];

    private static readonly OptionSpec StdinOption =
        new("--stdin", "Read the snippet from standard input");

    private const string InputNotes =
        "Input: pass a file, or --stdin to read standard input. With neither, standard input is\n" +
        "read only when it is redirected (piped); otherwise the command fails instead of waiting.";

    internal static readonly CommandSpec ProcessSpec = new()
    {
        Name = "process",
        Synopsis = "[<file>] [options]",
        Summary = "Compile a C# snippet and print hover, diagnostic and completion data as JSON.",
        Options = [StdinOption, .. CompileOptions],
        MaxPositionals = 1,
        Notes = InputNotes + "\n\nExit code 0 when JSON was produced (see meta.compileSucceeded); 1 on failure; 2 on usage errors.",
    };

    internal static readonly CommandSpec RenderSpec = new()
    {
        Name = "render",
        Synopsis = "[<file>] [options]",
        Summary = "Compile a C# snippet and render it as self-contained HTML with hover popups.",
        Options =
        [
            StdinOption,
            .. CompileOptions,
            new("--theme", "Color theme: " + string.Join(", ", GloSharpTheme.BuiltInNames) + " (default: github-dark)", "name"),
            new("--standalone", "Output a full HTML page instead of a fragment"),
            new("--output", "Write the HTML to a file instead of standard output", "path", "-o"),
        ],
        MaxPositionals = 1,
        Notes = InputNotes,
    };

    internal static readonly CommandSpec VerifySpec = new()
    {
        Name = "verify",
        Synopsis = "<path>... [options]",
        Summary = "Check that C# snippet files compile without unexpected errors (for CI).",
        Options = CompileOptions,
        MinPositionals = 1,
        MaxPositionals = int.MaxValue,
        Notes =
            "Each <path> is a .cs file or a directory searched recursively for *.cs (bin/, obj/,\n" +
            "node_modules/ and dot-directories are skipped). With --region, only files containing\n" +
            "exactly '#region <name>' are checked.\n\n" +
            "Errors are printed in MSBuild format, 'path(line,col): error CODE: message [path]', so\n" +
            "IDEs and GitHub problem matchers pick them up. Exit code 1 if any file fails or no files\n" +
            "were found.",
    };

    internal static readonly CommandSpec InitSpec = new()
    {
        Name = "init",
        Synopsis = "[--force]",
        Summary = "Create a glosharp.config.json with default settings in the current directory.",
        Options = [new("--force", "Overwrite an existing glosharp.config.json")],
    };

    internal static readonly CommandSpec CompactComplogSpec = new()
    {
        Name = "compact-complog",
        Synopsis = "<input> -o <output.glocontext> [options]",
        Summary = "Compact a .complog (or a .binlog from 'dotnet build -bl') into a small .glocontext.",
        Options =
        [
            new("--output", "Output .glocontext file (required)", "path", "-o"),
            new("--self-contained", "Embed all references (no targeting-pack pointers), for offline use"),
            new("--zstd-level", "Zstd compression level (default: 19)", "n"),
            new("--quiet", "Suppress the summary on standard error"),
            new("--keep-analyzers", "[debug] Keep analyzer entries in the payload"),
            new("--keep-sources", "[debug] Keep original source entries"),
            new("--keep-generated", "[debug] Keep generated source entries"),
            new("--no-refasm", "[debug] Skip reference-assembly rewriting"),
        ],
        MinPositionals = 1,
        MaxPositionals = 1,
        Notes = "Exit codes: 0 success, 1 other failure, 2 usage, 3 input not found, 4 I/O error,\n" +
                "5 reference rewriting failed, 6 invalid input data.",
    };

    internal static readonly IReadOnlyList<CommandSpec> Commands =
        [ProcessSpec, VerifySpec, RenderSpec, InitSpec, CompactComplogSpec];

    // ---------------------------------------------------------------------------------
    // Entry point
    // ---------------------------------------------------------------------------------

    public static async Task<int> RunAsync(string[] args, CliConsole console)
    {
        if (args.Length == 0)
        {
            console.Error.Write(GlobalHelp());
            return ExitCodes.Usage;
        }

        var command = args[0];
        switch (command)
        {
            case "--help" or "-h" or "-?":
                console.Out.Write(GlobalHelp());
                return ExitCodes.Success;
            case "--version":
                console.Out.WriteLine(Version());
                return ExitCodes.Success;
            case "help":
                if (args.Length > 1 && Commands.FirstOrDefault(c => c.Name == args[1]) is { } helpFor)
                    console.Out.Write(ArgumentParser.FormatHelp(helpFor));
                else
                    console.Out.Write(GlobalHelp());
                return ExitCodes.Success;
        }

        var spec = Commands.FirstOrDefault(c => c.Name == command);
        if (spec == null)
        {
            console.Error.WriteLine($"glosharp: error: unknown command '{command}'.");
            console.Error.WriteLine("Run 'glosharp --help' for the list of commands.");
            return ExitCodes.Usage;
        }

        ParsedCommand parsed;
        try
        {
            parsed = ArgumentParser.Parse(spec, args[1..]);
        }
        catch (UsageException ex)
        {
            return UsageError(console, spec, ex.Message);
        }

        if (parsed.HelpRequested)
        {
            console.Out.Write(ArgumentParser.FormatHelp(spec));
            return ExitCodes.Success;
        }

        try
        {
            return spec.Name switch
            {
                "process" => await RunProcess(parsed, console),
                "render" => await RunRender(parsed, console),
                "verify" => await RunVerify(parsed, console),
                "init" => RunInit(parsed.Has("--force"), console),
                "compact-complog" => RunCompactComplog(parsed, console),
                _ => throw new UnreachableException(),
            };
        }
        catch (UsageException ex)
        {
            return UsageError(console, spec, ex.Message);
        }
    }

    private static int UsageError(CliConsole console, CommandSpec spec, string message)
    {
        console.Error.WriteLine($"glosharp {spec.Name}: error: {message}");
        console.Error.WriteLine($"Usage: glosharp {spec.Name} {spec.Synopsis}");
        console.Error.WriteLine($"Run 'glosharp {spec.Name} --help' for more information.");
        return ExitCodes.Usage;
    }

    internal static string Version()
    {
        var assembly = typeof(CliApp).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString()
            ?? "0.0.0";
    }

    internal static string GlobalHelp()
    {
        var width = Commands.Max(c => c.Name.Length) + 2;
        var lines = new List<string>
        {
            "glosharp — compiler-driven type information for C# documentation (twoslash for C#)",
            "",
            "Usage: glosharp <command> [options]",
            "",
            "Commands:",
        };
        lines.AddRange(Commands.Select(c => $"  {c.Name.PadRight(width)}{c.Summary}"));
        lines.AddRange(
        [
            "",
            "Options:",
            "  -h, --help     Show help (also 'glosharp <command> --help')",
            "  --version      Show the glosharp version",
            "",
            "Environment:",
            "  GLOSHARP_CACHE_DIR          Cache for targeting packs and file-based app restores",
            $"  {ProcessRunner.TimeoutEnvironmentVariable,-27} Timeout in seconds for child processes such as 'dotnet restore' (default: 300)",
            "  DOTNET_ROOT                 .NET install to take reference assemblies from (default: the 'dotnet' on PATH)",
            "",
        ]);
        return string.Join(Environment.NewLine, lines) + Environment.NewLine;
    }

    // ---------------------------------------------------------------------------------
    // Shared settings: CLI options merged over glosharp.config.json
    // ---------------------------------------------------------------------------------

    internal sealed record CompileSettings
    {
        public string? Framework { get; init; }
        public string? Project { get; init; }
        public string? Complog { get; init; }
        public string? ComplogProject { get; init; }
        public string? Region { get; init; }
        public string? CacheDir { get; init; }
        public bool NoRestore { get; init; }
        public GloSharpConfig? Config { get; init; }

        public GloSharpProcessorOptions ToProcessorOptions(string? sourceFilePath) => new()
        {
            TargetFramework = Framework,
            ProjectPath = Project,
            RegionName = Region,
            SourceFilePath = sourceFilePath,
            NoRestore = NoRestore,
            CacheDir = CacheDir,
            ComplogPath = Complog,
            ComplogProject = ComplogProject,
            ImplicitUsings = Config?.ImplicitUsings,
            LangVersion = Config?.LangVersion,
            Nullable = Config?.Nullable,
        };
    }

    /// <summary>Thrown for failures that should print "error: …" and exit 1.</summary>
    private sealed class CliFailure(string message) : Exception(message);

    /// <summary>
    /// Loads the config (explicit --config, else discovered upwards from
    /// <paramref name="configStartDirectory"/>) and merges it under the CLI options. Choosing a
    /// compilation source on the command line (--project or --complog) replaces the config's
    /// source rather than conflicting with it.
    /// </summary>
    internal static CompileSettings LoadCompileSettings(ParsedCommand parsed, string configStartDirectory)
    {
        var cliProject = parsed.Get("--project");
        var cliComplog = parsed.Get("--complog");
        var cliComplogProject = parsed.Get("--complog-project");

        if (cliProject != null && cliComplog != null)
            throw new UsageException("--complog and --project are mutually exclusive.");
        if (cliProject != null && cliComplogProject != null)
            throw new UsageException("--complog-project requires --complog, not --project.");

        GloSharpConfig? config;
        try
        {
            config = ConfigLoader.Load(parsed.Get("--config"), configStartDirectory);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or UnauthorizedAccessException)
        {
            throw new CliFailure(ex.Message);
        }

        var cliChoseSource = cliProject != null || cliComplog != null;
        var project = cliProject ?? (cliChoseSource ? null : config?.Project);
        var complog = cliComplog ?? (cliChoseSource ? null : config?.Complog);
        var complogProject = cliComplogProject ?? (complog != null && cliComplog == null ? config?.ComplogProject : null);

        if (project != null && complog != null)
            throw new CliFailure("the config file sets both 'project' and 'complog'; they are mutually exclusive.");
        if (complogProject != null && complog == null)
            throw new UsageException("--complog-project requires --complog.");

        var framework = parsed.Get("--framework") ?? config?.Framework;
        if (framework != null)
        {
            try
            {
                FrameworkResolver.NormalizeTargetFramework(framework);
            }
            catch (InvalidOperationException ex)
            {
                throw new UsageException(ex.Message);
            }
        }

        return new CompileSettings
        {
            Framework = framework,
            Project = project,
            Complog = complog,
            ComplogProject = complogProject,
            Region = parsed.Get("--region"),
            CacheDir = parsed.Get("--cache-dir") ?? config?.CacheDir,
            NoRestore = parsed.Has("--no-restore") || config?.NoRestore == true,
            Config = config,
        };
    }

    /// <summary>
    /// Validates the --project path and restores it when its assets file is missing. Returns
    /// the project file to use.
    /// </summary>
    private static async Task<string> PrepareProjectAsync(string project, bool noRestore, CliConsole console)
    {
        string projectFile;
        try
        {
            projectFile = ProjectAssetsResolver.FindProjectFile(project);
        }
        catch (Exception ex) when (ex is ArgumentException or FileNotFoundException)
        {
            throw new CliFailure(ex.Message);
        }

        if (noRestore)
            return projectFile;

        var reason = ProjectAssetsResolver.GetRestoreReason(projectFile);
        if (reason == null)
            return projectFile;

        console.Error.WriteLine($"Running dotnet restore on {projectFile} ({reason})...");
        ProcessRunResult result;
        try
        {
            result = await ProcessRunner.RunAsync(
                FrameworkResolver.GetDotnetExecutable(),
                ["restore", projectFile],
                Path.GetDirectoryName(projectFile));
        }
        catch (InvalidOperationException ex)
        {
            throw new CliFailure($"failed to run dotnet restore: {ex.Message}");
        }

        if (!result.Succeeded)
        {
            var outcome = result.TimedOut
                ? $"timed out after {result.Elapsed.TotalSeconds:0}s (set {ProcessRunner.TimeoutEnvironmentVariable} to allow longer)"
                : $"failed with exit code {result.ExitCode}";
            throw new CliFailure($"dotnet restore {outcome}:\n{result.CombinedOutput.TrimEnd()}");
        }

        return projectFile;
    }

    private static async Task<(string Source, string? FilePath)> ReadInputAsync(ParsedCommand parsed, CliConsole console)
    {
        var filePath = parsed.Positionals.FirstOrDefault();
        var useStdin = parsed.Has("--stdin");

        if (filePath != null && useStdin)
            throw new UsageException("pass either a file or --stdin, not both.");

        if (filePath == null)
        {
            if (!useStdin && !console.IsInputRedirected)
                throw new UsageException("no input: pass a file, or --stdin to read the snippet from standard input.");
            return (await console.In.ReadToEndAsync(), null);
        }

        if (!File.Exists(filePath))
            throw new CliFailure($"file not found: {filePath}");
        return (await File.ReadAllTextAsync(filePath), filePath);
    }

    private static string ConfigStartDirectory(string? filePath) =>
        filePath != null
            ? Path.GetDirectoryName(Path.GetFullPath(filePath))!
            : Directory.GetCurrentDirectory();

    // ---------------------------------------------------------------------------------
    // Commands
    // ---------------------------------------------------------------------------------

    private static async Task<int> RunProcess(ParsedCommand parsed, CliConsole console)
    {
        try
        {
            var (source, filePath) = await ReadInputAsync(parsed, console);
            var settings = await PrepareAsync(LoadCompileSettings(parsed, ConfigStartDirectory(filePath)), console);

            var processor = new GloSharpProcessor();
            var result = await processor.ProcessAsync(source, settings.ToProcessorOptions(filePath));

            console.Out.WriteLine(JsonOutput.Serialize(result));
            // Exit 0 when JSON was produced; meta.compileSucceeded carries the compile status.
            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is not UsageException)
        {
            return Fail(console, "process", ex);
        }
    }

    private static async Task<int> RunRender(ParsedCommand parsed, CliConsole console)
    {
        try
        {
            var (source, filePath) = await ReadInputAsync(parsed, console);
            var settings = LoadCompileSettings(parsed, ConfigStartDirectory(filePath));

            var themeName = parsed.Get("--theme") ?? settings.Config?.Render?.Theme ?? "github-dark";
            var theme = GloSharpTheme.GetBuiltIn(themeName)
                ?? throw new UsageException(
                    $"unknown theme '{themeName}'. Valid themes: {string.Join(", ", GloSharpTheme.BuiltInNames)}");
            var standalone = parsed.Has("--standalone") || settings.Config?.Render?.Standalone == true;
            var outputPath = parsed.Get("--output");

            settings = await PrepareAsync(settings, console);
            var processor = new GloSharpProcessor();
            var processResult = await processor.ProcessWithContextAsync(source, settings.ToProcessorOptions(filePath));

            // Classify tokens for syntax highlighting
            var tokens = await SyntaxClassifier.ClassifyAsync(
                processResult.Result.Code,
                processResult.Compilation,
                processResult.SyntaxTree);

            var html = HtmlRenderer.Render(processResult.Result, tokens, theme, new HtmlRenderOptions
            {
                Standalone = standalone,
            });

            if (outputPath != null)
            {
                await File.WriteAllTextAsync(outputPath, html);
                console.Error.WriteLine($"Written to {outputPath}");
            }
            else
            {
                console.Out.Write(html);
            }

            return ExitCodes.Success;
        }
        catch (Exception ex) when (ex is not UsageException)
        {
            return Fail(console, "render", ex);
        }
    }

    /// <summary>Validates/restores --project; returns settings pointing at the project file.</summary>
    private static async Task<CompileSettings> PrepareAsync(CompileSettings settings, CliConsole console)
    {
        return settings.Project == null
            ? settings
            : settings with { Project = await PrepareProjectAsync(settings.Project, settings.NoRestore, console) };
    }

    private static int Fail(CliConsole console, string command, Exception ex)
    {
        console.Error.WriteLine($"glosharp {command}: error: {ex.Message}");
        return ExitCodes.Failure;
    }

    private static async Task<int> RunVerify(ParsedCommand parsed, CliConsole console)
    {
        var stopwatch = Stopwatch.StartNew();
        var region = parsed.Get("--region");

        List<string> files;
        try
        {
            files = VerifyInputs.Collect(parsed.Positionals);
        }
        catch (DirectoryNotFoundException ex)
        {
            throw new UsageException(ex.Message);
        }

        if (files.Count == 0)
        {
            console.Error.WriteLine(
                $"glosharp verify: error: no .cs files found in {string.Join(", ", parsed.Positionals.Select(p => $"'{p}'"))}.");
            return ExitCodes.Failure;
        }

        CompileSettings settings;
        try
        {
            // Config is discovered from the first input (a file's directory, or the directory).
            var first = parsed.Positionals[0];
            var startDir = Directory.Exists(first) ? Path.GetFullPath(first) : ConfigStartDirectory(first);
            settings = await PrepareAsync(LoadCompileSettings(parsed, startDir), console);
        }
        catch (Exception ex) when (ex is not UsageException)
        {
            return Fail(console, "verify", ex);
        }

        var processor = new GloSharpProcessor();
        var failed = new List<string>();
        var verified = 0;
        var skipped = 0;

        foreach (var file in files)
        {
            string source;
            try
            {
                source = await File.ReadAllTextAsync(file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(file);
                console.Out.WriteLine(DiagnosticFormatter.FormatFileFailure(file, ex.Message));
                continue;
            }

            if (region != null && !VerifyInputs.ContainsRegion(source, region))
            {
                skipped++;
                continue;
            }

            verified++;
            try
            {
                var result = await processor.ProcessAsync(source, settings.ToProcessorOptions(file));

                foreach (var warning in result.Meta.Warnings)
                {
                    console.Out.WriteLine(DiagnosticFormatter.FormatCanonical(
                        file, 1, 1, "warning", DiagnosticFormatter.ProcessingWarningCode, warning));
                }

                if (!result.Meta.CompileSucceeded)
                {
                    failed.Add(file);
                    var reported = 0;
                    foreach (var error in result.Errors.Where(e => !e.Expected && e.Severity == "error"))
                    {
                        console.Out.WriteLine(DiagnosticFormatter.Format(file, error));
                        reported++;
                    }

                    foreach (var error in result.HiddenErrors.Where(e => !e.Expected && e.Severity == "error"))
                    {
                        console.Out.WriteLine(DiagnosticFormatter.Format(file, error));
                        reported++;
                    }

                    if (reported == 0)
                    {
                        console.Out.WriteLine(DiagnosticFormatter.FormatCanonical(
                            file, 1, 1, "error", DiagnosticFormatter.VerificationFailedCode,
                            "the snippet failed to compile, but no visible error locates the problem " +
                            "(it may be in hidden/cut code); run 'glosharp process' on the file for details."));
                    }
                }
            }
            catch (Exception ex)
            {
                failed.Add(file);
                console.Out.WriteLine(DiagnosticFormatter.FormatFileFailure(file, ex.Message));
            }
        }

        stopwatch.Stop();
        var skippedNote = skipped > 0 ? $", {skipped} skipped (no '#region {region}')" : "";
        if (verified == 0)
        {
            console.Error.WriteLine($"glosharp verify: error: none of the {files.Count} file(s) contain '#region {region}'.");
            return ExitCodes.Failure;
        }

        if (failed.Count > 0)
        {
            console.Out.WriteLine();
            console.Out.WriteLine($"{failed.Count} of {verified} file(s) failed verification{skippedNote} ({stopwatch.Elapsed.TotalSeconds:0.0}s):");
            foreach (var f in failed)
                console.Out.WriteLine($"  {f}");
            return ExitCodes.Failure;
        }

        console.Out.WriteLine($"All {verified} file(s) verified successfully{skippedNote} ({stopwatch.Elapsed.TotalSeconds:0.0}s).");
        return ExitCodes.Success;
    }

    private static int RunInit(bool force, CliConsole console)
    {
        try
        {
            ConfigLoader.WriteDefault(Directory.GetCurrentDirectory(), force);
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("--force"))
        {
            console.Error.WriteLine($"glosharp init: error: {ex.Message}");
            return ExitCodes.Failure;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            console.Error.WriteLine($"glosharp init: error: {ex.Message}");
            return ExitCodes.Failure;
        }

        console.Error.WriteLine($"Created {ConfigLoader.ConfigFileName}");
        console.Error.WriteLine();
        console.Error.Write(ConfigPropertiesHelp);
        return ExitCodes.Success;
    }

    internal const string ConfigPropertiesHelp =
        "Properties:\n" +
        "  framework         Target framework moniker (default: " + FrameworkResolver.DefaultTargetFramework + ")\n" +
        "  project           Path to a .csproj (or its directory) to compile against\n" +
        "  complog           Path to a .complog/.glocontext for portable compilation\n" +
        "  complogProject    Compilation to select from a multi-project complog\n" +
        "  cacheDir          Directory for disk-based result caching\n" +
        "  noRestore         Skip automatic dotnet restore (true/false)\n" +
        "  implicitUsings    Global usings to apply (replaces the defaults)\n" +
        "  langVersion       C# language version (e.g. 12, latest, preview)\n" +
        "  nullable          Nullable context (enable, disable, warnings, annotations)\n" +
        "  render.theme      Color theme (github-dark, github-light)\n" +
        "  render.standalone Output a full HTML page (true/false)\n";

    private static int RunCompactComplog(ParsedCommand parsed, CliConsole console)
    {
        var input = parsed.Positionals[0];
        var output = parsed.Get("--output")
            ?? throw new UsageException("missing required option --output (-o) <path>.");

        var zstdLevel = 19;
        if (parsed.Get("--zstd-level") is { } levelText && !int.TryParse(levelText, out zstdLevel))
            throw new UsageException($"invalid --zstd-level value: '{levelText}' (expected an integer).");

        var quiet = parsed.Has("--quiet");
        var options = new ComplogCompactionOptions
        {
            RewriteReferences = !parsed.Has("--no-refasm"),
            DropAnalyzers = !parsed.Has("--keep-analyzers"),
            DropOriginalSources = !parsed.Has("--keep-sources"),
            DropGeneratedSources = !parsed.Has("--keep-generated"),
            SelfContained = parsed.Has("--self-contained"),
            ZstdLevel = zstdLevel,
        };

        try
        {
            var result = ComplogCompactor.Compact(input, output, options);
            if (!quiet)
            {
                var ratio = result.InputSizeBytes > 0
                    ? (double)result.OutputSizeBytes / result.InputSizeBytes
                    : 0.0;
                console.Error.WriteLine($"compact-complog: {input} → {output}");
                console.Error.WriteLine($"  input:  {result.InputSizeBytes:N0} bytes");
                console.Error.WriteLine($"  output: {result.OutputSizeBytes:N0} bytes ({ratio:P1} of input)");
                console.Error.WriteLine($"  references: {result.ReferencesBefore} → {result.ReferencesAfter} unique blobs");
                console.Error.WriteLine($"  refasm rewrites: {result.RefasmRewrittenCount}");
                var packList = result.PointerPacks.Count > 0
                    ? string.Join(", ", result.PointerPacks)
                    : "none";
                console.Error.WriteLine($"  pointers: {result.PointersCreated} (packs: {packList})");
                console.Error.WriteLine($"  dropped: {result.AnalyzersDropped} analyzers, {result.OriginalSourcesDropped} sources, {result.GeneratedSourcesDropped} generated");
            }
            // Warnings are shown even with --quiet: they mean the output is degraded.
            foreach (var warning in result.Warnings)
                console.Error.WriteLine($"  warning: {warning}");
            return ExitCodes.Success;
        }
        catch (FileNotFoundException ex)
        {
            console.Error.WriteLine($"compact-complog: {ex.Message}");
            return 3;
        }
        catch (IOException ex)
        {
            console.Error.WriteLine($"compact-complog: {ex.Message}");
            return 4;
        }
        catch (InvalidOperationException ex) when (ex.Message.Contains("Refasmer", StringComparison.Ordinal))
        {
            console.Error.WriteLine($"compact-complog: {ex.Message}");
            return 5;
        }
        catch (InvalidDataException ex)
        {
            console.Error.WriteLine($"compact-complog: {ex.Message}");
            return 6;
        }
        catch (Exception ex)
        {
            console.Error.WriteLine($"compact-complog: {ex.GetType().Name}: {ex.Message}");
            return ExitCodes.Failure;
        }
    }
}

/// <summary>Collects the files <c>glosharp verify</c> checks.</summary>
internal static class VerifyInputs
{
    private static readonly HashSet<string> ExcludedDirectories =
        new(StringComparer.OrdinalIgnoreCase) { "bin", "obj", "node_modules" };

    /// <summary>
    /// Expands files and directories (recursively, skipping bin/, obj/, node_modules/ and
    /// dot-directories) into a sorted, de-duplicated list of full paths. Throws
    /// <see cref="DirectoryNotFoundException"/> for a path that doesn't exist.
    /// </summary>
    public static List<string> Collect(IEnumerable<string> paths)
    {
        var files = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var path in paths)
        {
            if (File.Exists(path))
            {
                files.Add(Path.GetFullPath(path));
            }
            else if (Directory.Exists(path))
            {
                foreach (var file in EnumerateCs(Path.GetFullPath(path)))
                    files.Add(file);
            }
            else
            {
                throw new DirectoryNotFoundException(
                    path.Contains('*') || path.Contains('?')
                        ? $"path not found: '{path}' (wildcards are expanded by your shell, not by glosharp; pass directories or files)."
                        : $"path not found: '{path}'.");
            }
        }
        return files.ToList();
    }

    private static IEnumerable<string> EnumerateCs(string directory)
    {
        foreach (var file in Directory.EnumerateFiles(directory, "*.cs"))
            yield return file;

        foreach (var sub in Directory.EnumerateDirectories(directory))
        {
            var name = Path.GetFileName(sub);
            if (name.StartsWith('.') || ExcludedDirectories.Contains(name))
                continue;
            foreach (var file in EnumerateCs(sub))
                yield return file;
        }
    }

    /// <summary>True if the source has a line that is exactly <c>#region &lt;name&gt;</c> (ignoring surrounding whitespace).</summary>
    public static bool ContainsRegion(string source, string regionName)
    {
        var pattern = $@"^[ \t]*#region[ \t]+{Regex.Escape(regionName.Trim())}[ \t]*\r?$";
        return Regex.IsMatch(source, pattern, RegexOptions.Multiline);
    }
}
