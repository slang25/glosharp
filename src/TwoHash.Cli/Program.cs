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
            case "--no-restore":
                noRestore = true;
                break;
            default:
                if (!args[i].StartsWith('-'))
                    filePath = args[i];
                break;
        }
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

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: twohash <command> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  process <file>    Process a C# file and output JSON metadata");
    Console.Error.WriteLine("  verify <dir>      Verify all .cs files in a directory compile");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --stdin             Read source from stdin");
    Console.Error.WriteLine("  --framework <tfm>   Target framework (e.g., net8.0)");
    Console.Error.WriteLine("  --project <path>    Project (.csproj or directory) for NuGet resolution");
    Console.Error.WriteLine("  --region <name>     Extract a named #region from the source file");
    Console.Error.WriteLine("  --no-restore        Skip automatic dotnet restore");
    Console.Error.WriteLine("  --cache-dir <path>  Directory for disk-based result caching");
}

static int PrintUsageAndReturn() { PrintUsage(); return 0; }
static int PrintUnknownCommand(string cmd) { Console.Error.WriteLine($"Unknown command: {cmd}"); PrintUsage(); return 1; }
