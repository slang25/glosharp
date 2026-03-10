using System.Diagnostics;
using TwoHash.Core;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];

return command switch
{
    "process" => await RunProcess(args[1..]),
    "verify" => await RunVerify(args[1..]),
    "render" => await RunRender(args[1..]),
    "init" => RunInit(args[1..]),
    "--help" or "-h" => PrintUsageAndReturn(),
    _ => PrintUnknownCommand(command),
};

static async Task<int> RunProcess(string[] args)
{
    string? filePath = null;
    string? framework = null;
    string? project = null;
    string? region = null;
    string? cacheDir = null;
    string? configPath = null;
    string? complog = null;
    string? complogProject = null;
    var useStdin = false;
    var noRestore = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--stdin":
                useStdin = true;
                break;
            case "--framework" when i + 1 < args.Length:
                framework = args[++i];
                break;
            case "--project" when i + 1 < args.Length:
                project = args[++i];
                break;
            case "--region" when i + 1 < args.Length:
                region = args[++i];
                break;
            case "--cache-dir" when i + 1 < args.Length:
                cacheDir = args[++i];
                break;
            case "--config" when i + 1 < args.Length:
                configPath = args[++i];
                break;
            case "--complog" when i + 1 < args.Length:
                complog = args[++i];
                break;
            case "--complog-project" when i + 1 < args.Length:
                complogProject = args[++i];
                break;
            case "--no-restore":
                noRestore = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                    filePath = args[i];
                break;
        }
    }

    // Load config file
    TwohashConfig? config;
    try
    {
        var startDir = filePath != null
            ? Path.GetDirectoryName(Path.GetFullPath(filePath))!
            : Directory.GetCurrentDirectory();
        config = ConfigLoader.Load(configPath, startDir);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    // Merge: CLI args win over config
    if (config != null)
    {
        framework ??= config.Framework;
        project ??= config.Project;
        cacheDir ??= config.CacheDir;
        complog ??= config.Complog;
        complogProject ??= config.ComplogProject;
        if (!noRestore && config.NoRestore == true)
            noRestore = true;
    }

    // Validate mutual exclusivity
    if (complog != null && project != null)
    {
        Console.Error.WriteLine("Error: --complog and --project are mutually exclusive");
        return 1;
    }
    if (complogProject != null && complog == null)
    {
        Console.Error.WriteLine("Error: --complog-project requires --complog");
        return 1;
    }

    // Validate --region is not used with --stdin
    if (region != null && (useStdin || filePath == null))
    {
        Console.Error.WriteLine("Error: --region cannot be used with --stdin (region extraction requires a file)");
        return 1;
    }

    string source;
    if (useStdin || filePath == null)
    {
        source = Console.In.ReadToEnd();
    }
    else
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: file not found: {filePath}");
            return 1;
        }
        source = File.ReadAllText(filePath);
    }

    // Auto-restore if project specified and assets file missing
    if (project != null && !noRestore)
    {
        if (!TryEnsureRestored(project))
            return 1;
    }

    try
    {
        var processor = new TwohashProcessor();
        var result = await processor.ProcessAsync(source, new TwohashProcessorOptions
        {
            TargetFramework = framework,
            ProjectPath = project,
            RegionName = region,
            SourceFilePath = filePath,
            NoRestore = noRestore,
            CacheDir = cacheDir,
            ComplogPath = complog,
            ComplogProject = complogProject,
        });

        Console.Write(JsonOutput.Serialize(result));

        return result.Meta.CompileSucceeded ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static async Task<int> RunVerify(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Error: verify requires a directory path");
        return 1;
    }

    var directory = args[0];
    string? framework = null;
    string? project = null;
    string? region = null;
    string? cacheDir = null;
    string? configPath = null;
    string? complog = null;
    string? complogProject = null;
    var noRestore = false;

    for (var i = 1; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--framework" when i + 1 < args.Length:
                framework = args[++i];
                break;
            case "--project" when i + 1 < args.Length:
                project = args[++i];
                break;
            case "--region" when i + 1 < args.Length:
                region = args[++i];
                break;
            case "--cache-dir" when i + 1 < args.Length:
                cacheDir = args[++i];
                break;
            case "--config" when i + 1 < args.Length:
                configPath = args[++i];
                break;
            case "--complog" when i + 1 < args.Length:
                complog = args[++i];
                break;
            case "--complog-project" when i + 1 < args.Length:
                complogProject = args[++i];
                break;
            case "--no-restore":
                noRestore = true;
                break;
        }
    }

    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"Error: directory not found: {directory}");
        return 1;
    }

    // Load config file
    TwohashConfig? config;
    try
    {
        var startDir = Path.GetFullPath(directory);
        config = ConfigLoader.Load(configPath, startDir);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    // Merge: CLI args win over config
    if (config != null)
    {
        framework ??= config.Framework;
        project ??= config.Project;
        cacheDir ??= config.CacheDir;
        complog ??= config.Complog;
        complogProject ??= config.ComplogProject;
        if (!noRestore && config.NoRestore == true)
            noRestore = true;
    }

    // Validate mutual exclusivity
    if (complog != null && project != null)
    {
        Console.Error.WriteLine("Error: --complog and --project are mutually exclusive");
        return 1;
    }
    if (complogProject != null && complog == null)
    {
        Console.Error.WriteLine("Error: --complog-project requires --complog");
        return 1;
    }

    // Auto-restore if project specified and assets file missing
    if (project != null && !noRestore)
    {
        if (!TryEnsureRestored(project))
            return 1;
    }

    var files = Directory.GetFiles(directory, "*.cs", SearchOption.AllDirectories);
    var processor = new TwohashProcessor();
    var failures = new List<string>();

    foreach (var file in files)
    {
        try
        {
            var source = File.ReadAllText(file);

            // If region is specified, skip files that don't contain the region
            if (region != null && !source.Contains($"#region {region}"))
                continue;

            var result = await processor.ProcessAsync(source, new TwohashProcessorOptions
            {
                TargetFramework = framework,
                ProjectPath = project,
                RegionName = region,
                SourceFilePath = file,
                NoRestore = noRestore,
                CacheDir = cacheDir,
                ComplogPath = complog,
                ComplogProject = complogProject,
            });
            if (!result.Meta.CompileSucceeded)
            {
                failures.Add(file);
                var unexpectedErrors = result.Errors.Where(e => !e.Expected && e.Severity == "error");
                foreach (var error in unexpectedErrors)
                {
                    Console.Error.WriteLine($"  {file}({error.Line + 1},{error.Character + 1}): {error.Code} {error.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            failures.Add(file);
            Console.Error.WriteLine($"  {file}: {ex.Message}");
        }
    }

    if (failures.Count > 0)
    {
        Console.Error.WriteLine($"\n{failures.Count} file(s) failed verification:");
        foreach (var f in failures)
            Console.Error.WriteLine($"  {f}");
        return 1;
    }

    Console.Error.WriteLine($"All {files.Length} file(s) verified successfully.");
    return 0;
}

static bool TryEnsureRestored(string projectPath)
{
    try
    {
        ProjectAssetsResolver.FindAssetsFile(projectPath);
        return true; // Assets file exists
    }
    catch (FileNotFoundException)
    {
        // Assets file missing, run dotnet restore
        Console.Error.WriteLine($"Running dotnet restore on {projectPath}...");
        var psi = new ProcessStartInfo("dotnet", $"restore \"{projectPath}\"")
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        try
        {
            var process = Process.Start(psi);
            if (process == null)
            {
                Console.Error.WriteLine("Error: failed to start dotnet restore");
                return false;
            }

            process.WaitForExit();
            if (process.ExitCode != 0)
            {
                var stderr = process.StandardError.ReadToEnd();
                Console.Error.WriteLine($"Error: dotnet restore failed:\n{stderr}");
                return false;
            }

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"Error: failed to run dotnet restore: {ex.Message}");
            return false;
        }
    }
}

static async Task<int> RunRender(string[] args)
{
    string? filePath = null;
    string? framework = null;
    string? project = null;
    string? region = null;
    string? cacheDir = null;
    string? themeName = null;
    string? outputPath = null;
    string? configPath = null;
    string? complog = null;
    string? complogProject = null;
    var useStdin = false;
    var noRestore = false;
    var standalone = false;

    for (var i = 0; i < args.Length; i++)
    {
        switch (args[i])
        {
            case "--stdin":
                useStdin = true;
                break;
            case "--framework" when i + 1 < args.Length:
                framework = args[++i];
                break;
            case "--project" when i + 1 < args.Length:
                project = args[++i];
                break;
            case "--region" when i + 1 < args.Length:
                region = args[++i];
                break;
            case "--cache-dir" when i + 1 < args.Length:
                cacheDir = args[++i];
                break;
            case "--theme" when i + 1 < args.Length:
                themeName = args[++i];
                break;
            case "--output" when i + 1 < args.Length:
                outputPath = args[++i];
                break;
            case "--config" when i + 1 < args.Length:
                configPath = args[++i];
                break;
            case "--complog" when i + 1 < args.Length:
                complog = args[++i];
                break;
            case "--complog-project" when i + 1 < args.Length:
                complogProject = args[++i];
                break;
            case "--standalone":
                standalone = true;
                break;
            case "--no-restore":
                noRestore = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                    filePath = args[i];
                break;
        }
    }

    // Load config file
    TwohashConfig? config;
    try
    {
        var startDir = filePath != null
            ? Path.GetDirectoryName(Path.GetFullPath(filePath))!
            : Directory.GetCurrentDirectory();
        config = ConfigLoader.Load(configPath, startDir);
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }

    // Merge: CLI args win over config
    if (config != null)
    {
        framework ??= config.Framework;
        project ??= config.Project;
        cacheDir ??= config.CacheDir;
        complog ??= config.Complog;
        complogProject ??= config.ComplogProject;
        if (!noRestore && config.NoRestore == true)
            noRestore = true;
        themeName ??= config.Render?.Theme;
        if (!standalone && config.Render?.Standalone == true)
            standalone = true;
    }

    // Validate mutual exclusivity
    if (complog != null && project != null)
    {
        Console.Error.WriteLine("Error: --complog and --project are mutually exclusive");
        return 1;
    }
    if (complogProject != null && complog == null)
    {
        Console.Error.WriteLine("Error: --complog-project requires --complog");
        return 1;
    }

    // Validate theme
    themeName ??= "github-dark";
    var theme = TwohashTheme.GetBuiltIn(themeName);
    if (theme == null)
    {
        Console.Error.WriteLine($"Error: unknown theme '{themeName}'. Valid themes: {string.Join(", ", TwohashTheme.BuiltInNames)}");
        return 1;
    }

    // Validate --region with --stdin
    if (region != null && (useStdin || filePath == null))
    {
        Console.Error.WriteLine("Error: --region cannot be used with --stdin (region extraction requires a file)");
        return 1;
    }

    string source;
    if (useStdin || filePath == null)
    {
        source = Console.In.ReadToEnd();
    }
    else
    {
        if (!File.Exists(filePath))
        {
            Console.Error.WriteLine($"Error: file not found: {filePath}");
            return 1;
        }
        source = File.ReadAllText(filePath);
    }

    // Auto-restore if project specified and assets file missing
    if (project != null && !noRestore)
    {
        if (!TryEnsureRestored(project))
            return 1;
    }

    try
    {
        var processor = new TwohashProcessor();
        var processResult = await processor.ProcessWithContextAsync(source, new TwohashProcessorOptions
        {
            TargetFramework = framework,
            ProjectPath = project,
            RegionName = region,
            SourceFilePath = filePath,
            NoRestore = noRestore,
            CacheDir = cacheDir,
            ComplogPath = complog,
            ComplogProject = complogProject,
        });

        // Classify tokens for syntax highlighting
        var tokens = await SyntaxClassifier.ClassifyAsync(
            processResult.Result.Code,
            processResult.Compilation,
            processResult.SyntaxTree);

        // Render HTML
        var html = HtmlRenderer.Render(processResult.Result, tokens, theme, new HtmlRenderOptions
        {
            Standalone = standalone,
        });

        if (outputPath != null)
        {
            File.WriteAllText(outputPath, html);
            Console.Error.WriteLine($"Written to {outputPath}");
        }
        else
        {
            Console.Write(html);
        }

        return processResult.Result.Meta.CompileSucceeded ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static int RunInit(string[] args)
{
    var force = args.Contains("--force");

    try
    {
        ConfigLoader.WriteDefault(Directory.GetCurrentDirectory(), force);
        Console.Error.WriteLine("Created twohash.config.json");
        Console.Error.WriteLine();
        Console.Error.WriteLine("Properties:");
        Console.Error.WriteLine("  framework    - Target framework moniker (e.g., net9.0)");
        Console.Error.WriteLine("  project      - Path to .csproj or directory for NuGet resolution");
        Console.Error.WriteLine("  cacheDir     - Directory for disk-based result caching");
        Console.Error.WriteLine("  noRestore    - Skip automatic dotnet restore (true/false)");
        Console.Error.WriteLine("  complog      - Path to .complog file for portable compilation");
        Console.Error.WriteLine("  complogProject - Project name to select from multi-project complog");
        Console.Error.WriteLine("  render.theme - Color theme (github-dark, github-light)");
        Console.Error.WriteLine("  render.standalone - Output full HTML page (true/false)");
        return 0;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: twohash <command> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  process <file>    Process a C# file and output JSON metadata");
    Console.Error.WriteLine("  verify <dir>      Verify all .cs files in a directory compile");
    Console.Error.WriteLine("  render <file>     Render a C# file as self-contained HTML");
    Console.Error.WriteLine("  init              Create a twohash.config.json with defaults");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --stdin             Read source from stdin");
    Console.Error.WriteLine("  --framework <tfm>   Target framework (e.g., net8.0)");
    Console.Error.WriteLine("  --project <path>    Project (.csproj or directory) for NuGet resolution");
    Console.Error.WriteLine("  --region <name>     Extract a named #region from the source file");
    Console.Error.WriteLine("  --no-restore        Skip automatic dotnet restore");
    Console.Error.WriteLine("  --cache-dir <path>  Directory for disk-based result caching");
    Console.Error.WriteLine("  --complog <path>    Complog file for portable compilation (no SDK needed)");
    Console.Error.WriteLine("  --complog-project <name>  Project to select from multi-project complog");
    Console.Error.WriteLine("  --config <path>     Path to twohash.config.json (default: auto-discover)");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Render options:");
    Console.Error.WriteLine("  --theme <name>      Color theme (github-dark, github-light; default: github-dark)");
    Console.Error.WriteLine("  --standalone        Output a full HTML page instead of a fragment");
    Console.Error.WriteLine("  --output <path>     Write HTML to a file instead of stdout");
}

static int PrintUsageAndReturn() { PrintUsage(); return 0; }
static int PrintUnknownCommand(string cmd) { Console.Error.WriteLine($"Unknown command: {cmd}"); PrintUsage(); return 1; }
