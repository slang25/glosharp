using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GloSharp.Core;

public class ResultCache
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    private readonly string _cacheDir;

    public ResultCache(string cacheDir)
    {
        _cacheDir = cacheDir;
    }

    public GloSharpResult? TryGet(string key)
    {
        var path = GetCachePath(key);
        if (!File.Exists(path))
            return null;

        try
        {
            var json = File.ReadAllText(path);
            return JsonSerializer.Deserialize<GloSharpResult>(json, JsonOptions);
        }
        catch (JsonException)
        {
            // Corrupt cache file — treat as miss
            return null;
        }
    }

    public void Set(string key, GloSharpResult result)
    {
        Directory.CreateDirectory(_cacheDir);

        var json = JsonSerializer.Serialize(result, JsonOptions);
        var finalPath = GetCachePath(key);
        var tempPath = finalPath + ".tmp." + Guid.NewGuid().ToString("N")[..8];

        try
        {
            File.WriteAllText(tempPath, json);
            File.Move(tempPath, finalPath, overwrite: true);
        }
        catch
        {
            // Clean up temp file on failure
            try { File.Delete(tempPath); } catch { /* best effort */ }
            throw;
        }
    }

    public static string ComputeKey(
        string source,
        string targetFramework,
        List<PackageReference>? packages,
        string? projectPath)
    {
        using var sha256 = SHA256.Create();
        var sb = new StringBuilder();

        sb.Append(VersionInfo.GetVersion());
        sb.Append('\0');
        sb.Append(targetFramework);
        sb.Append('\0');

        if (packages is { Count: > 0 })
        {
            var sorted = packages
                .OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase)
                .Select(p => $"{p.Name}@{p.Version}");
            sb.Append(string.Join(",", sorted));
        }
        sb.Append('\0');

        sb.Append(projectPath ?? "");
        sb.Append('\0');
        sb.Append(source);

        var bytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private string GetCachePath(string key) => Path.Combine(_cacheDir, $"{key}.json");
}
