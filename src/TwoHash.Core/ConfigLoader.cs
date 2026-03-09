using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwoHash.Core;

public class TwohashConfig
{
    [JsonPropertyName("framework")]
    public string? Framework { get; set; }

    [JsonPropertyName("project")]
    public string? Project { get; set; }

    [JsonPropertyName("cacheDir")]
    public string? CacheDir { get; set; }

    [JsonPropertyName("noRestore")]
    public bool? NoRestore { get; set; }

    [JsonPropertyName("render")]
    public TwohashRenderConfig? Render { get; set; }
}

public class TwohashRenderConfig
{
    [JsonPropertyName("theme")]
    public string? Theme { get; set; }

    [JsonPropertyName("standalone")]
    public bool? Standalone { get; set; }
}

public static class ConfigLoader
{
    public const string ConfigFileName = "twohash.config.json";

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>
    /// Loads a twohash config file. If explicitPath is provided, loads that file directly.
    /// Otherwise, walks up from startDirectory to find twohash.config.json.
    /// Returns null if no config file is found (and no explicit path was given).
    /// </summary>
    public static TwohashConfig? Load(string? explicitPath, string startDirectory)
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
        TwohashConfig config;
        try
        {
            config = JsonSerializer.Deserialize<TwohashConfig>(json, JsonOptions)
                ?? new TwohashConfig();
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
    /// Walks up from startDirectory looking for twohash.config.json.
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

    private static void ResolveRelativePaths(TwohashConfig config, string configDir)
    {
        if (config.Project != null && !Path.IsPathRooted(config.Project))
            config.Project = Path.GetFullPath(Path.Combine(configDir, config.Project));

        if (config.CacheDir != null && !Path.IsPathRooted(config.CacheDir))
            config.CacheDir = Path.GetFullPath(Path.Combine(configDir, config.CacheDir));
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

        var config = new TwohashConfig
        {
            Framework = "net9.0",
            Project = null,
            CacheDir = null,
            NoRestore = false,
            Render = new TwohashRenderConfig
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
