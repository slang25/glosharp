using System.Text.Json;
using System.Text.Json.Serialization;

namespace GloSharp.Core;

public class GloSharpConfig
{
    [JsonPropertyName("framework")]
    public string? Framework { get; set; }

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("cacheDir")]
    public string? CacheDir { get; set; }

    [JsonPropertyName("noRestore")]
    public bool? NoRestore { get; set; }

    [JsonPropertyName("complog")]
    public string? Complog { get; set; }

    [JsonPropertyName("complogProject")]
    public string? ComplogProject { get; set; }

    [JsonPropertyName("implicitUsings")]
    public string[]? ImplicitUsings { get; set; }

    [JsonPropertyName("langVersion")]
    public string? LangVersion { get; set; }

    [JsonPropertyName("nullable")]
    public string? Nullable { get; set; }

    [JsonPropertyName("render")]
    public GloSharpRenderConfig? Render { get; set; }
}

public class GloSharpRenderConfig
{
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("standalone")]
    public bool? Standalone { get; set; }
}

public static class ConfigLoader
{
    public const string ConfigFileName = "glosharp.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads a glosharp config file. If explicitPath is provided, loads that file directly.
    /// Otherwise, walks up from startDirectory to find glosharp.config.json.
    /// Returns null if no config file is found (and no explicit path was given).
    /// </summary>
    public static GloSharpConfig? Load(string? explicitPath, string startDirectory)
    {
        string? configPath;

        if (explicitPath != null)
        {
            configPath = Path.GetFullPath(explicitPath);
            if (!File.Exists(configPath))
                throw new FileNotFoundException($"Config file not found: {explicitPath}");
        }
        else
        {
            configPath = Discover(startDirectory);
            if (configPath == null)
                return null;
        }

        var json = File.ReadAllText(configPath);
        GloSharpConfig config;
        try
        {
            config = JsonSerializer.Deserialize<GloSharpConfig>(json, JsonOptions)
                ?? new GloSharpConfig();
        }
        catch (JsonException ex)
        {
            throw new InvalidOperationException(
                $"Invalid JSON in config file '{configPath}': {ex.Message}", ex);
        }

        // Resolve relative paths relative to config file directory
        var configDir = Path.GetDirectoryName(configPath)!;
        ResolveRelativePaths(config, configDir);

        return config;
    }

    /// <summary>
    /// Walks up from startDirectory looking for glosharp.config.json.
    /// Returns the full path if found, null otherwise.
    /// </summary>
    public static string? Discover(string startDirectory)
    {
        var dir = Path.GetFullPath(startDirectory);

        while (true)
        {
            var candidate = Path.Combine(dir, ConfigFileName);
            if (File.Exists(candidate))
                return candidate;

            var parent = Directory.GetParent(dir);
            if (parent == null)
                return null;

            dir = parent.FullName;
        }
    }

    private static void ResolveRelativePaths(GloSharpConfig config, string configDir)
    {
        if (config.Project != null && !Path.IsPathRooted(config.Project))
            config.Project = Path.GetFullPath(Path.Combine(configDir, config.Project));

        if (config.CacheDir != null && !Path.IsPathRooted(config.CacheDir))
            config.CacheDir = Path.GetFullPath(Path.Combine(configDir, config.CacheDir));

        if (config.Complog != null && !Path.IsPathRooted(config.Complog))
            config.Complog = Path.GetFullPath(Path.Combine(configDir, config.Complog));
    }

    /// <summary>
    /// Writes a default config file to the specified directory.
    /// </summary>
    public static void WriteDefault(string directory, bool force = false)
    {
        var path = Path.Combine(directory, ConfigFileName);

        if (File.Exists(path) && !force)
            throw new InvalidOperationException(
                $"Config file already exists: {path}. Use --force to overwrite.");

        var config = new GloSharpConfig
        {
            Framework = "net9.0",
            Project = null,
            CacheDir = null,
            NoRestore = false,
            Complog = null,
            ComplogProject = null,
            Render = new GloSharpRenderConfig
            {
                Theme = "github-dark",
                Standalone = false,
            },
        };

        var options = new JsonSerializerOptions
        {
            WriteIndented = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
        };

        var json = JsonSerializer.Serialize(config, options);
        File.WriteAllText(path, json + Environment.NewLine);
    }
}
