using TwoHash.Core;

if (args.Length == 0)
{
    PrintUsage();
    return 1;
}

var command = args[0];

return command switch
{
    "process" => RunProcess(args[1..]),
    "verify" => RunVerify(args[1..]),
    "--help" or "-h" => PrintUsageAndReturn(),
    _ => PrintUnknownCommand(command),
};

static int RunProcess(string[] args)
{
    string? filePath = null;
    string? framework = null;
    var useStdin = false;

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
            default:
                if (!args[i].StartsWith('-'))
                    filePath = args[i];
                break;
        }
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

    try
    {
        var processor = new TwohashProcessor();
        var result = processor.Process(source, new TwohashProcessorOptions { TargetFramework = framework });

        Console.Write(JsonOutput.Serialize(result));

        return result.Meta.CompileSucceeded ? 0 : 1;
    }
    catch (Exception ex)
    {
        Console.Error.WriteLine($"Error: {ex.Message}");
        return 1;
    }
}

static int RunVerify(string[] args)
{
    if (args.Length == 0)
    {
        Console.Error.WriteLine("Error: verify requires a directory path");
        return 1;
    }

    var directory = args[0];
    string? framework = null;

    for (var i = 1; i < args.Length; i++)
    {
        if (args[i] == "--framework" && i + 1 < args.Length)
            framework = args[++i];
    }

    if (!Directory.Exists(directory))
    {
        Console.Error.WriteLine($"Error: directory not found: {directory}");
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
            var result = processor.Process(source, new TwohashProcessorOptions { TargetFramework = framework });
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

static void PrintUsage()
{
    Console.Error.WriteLine("Usage: twohash <command> [options]");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Commands:");
    Console.Error.WriteLine("  process <file>    Process a C# file and output JSON metadata");
    Console.Error.WriteLine("  verify <dir>      Verify all .cs files in a directory compile");
    Console.Error.WriteLine();
    Console.Error.WriteLine("Options:");
    Console.Error.WriteLine("  --stdin           Read source from stdin");
    Console.Error.WriteLine("  --framework <tfm> Target framework (e.g., net8.0)");
}

static int PrintUsageAndReturn() { PrintUsage(); return 0; }
static int PrintUnknownCommand(string cmd) { Console.Error.WriteLine($"Unknown command: {cmd}"); PrintUsage(); return 1; }
