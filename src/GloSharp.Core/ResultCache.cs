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
        catch (Exception ex) when (ex is JsonException or IOException)
        {
            // Corrupt or concurrently-replaced cache file — treat as miss
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

    private static readonly JsonSerializerOptions KeyJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false,
    };

    /// <summary>
    /// Computes the cache key for a snippet.
    /// </summary>
    /// <remarks>
    /// Key completeness is structural: the whole effective options record is serialised, so any
    /// option added to <see cref="GloSharpProcessorOptions"/> automatically becomes part of the
    /// key. <see cref="GloSharpProcessorOptions.CacheDir"/> is excluded (it only says where the
    /// cache lives). <paramref name="contextFingerprints"/> carries fingerprints of on-disk inputs
    /// that are referenced by path (project assets file, complog/.glocontext), so rebuilding or
    /// re-restoring them invalidates the entry.
    /// </remarks>
    public static string ComputeKey(
        string source,
        GloSharpProcessorOptions options,
        IEnumerable<string>? contextFingerprints = null)
    {
        var sb = new StringBuilder();

        sb.Append(VersionInfo.GetVersion());
        sb.Append('\0');
        sb.Append(JsonSerializer.Serialize(options with { CacheDir = null }, KeyJsonOptions));
        sb.Append('\0');

        foreach (var fingerprint in contextFingerprints ?? [])
        {
            sb.Append(fingerprint);
            sb.Append('\n');
        }
        sb.Append('\0');
        sb.Append(source);

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>
    /// A cheap change-detection fingerprint for a file: full path, size and last-write time.
    /// </summary>
    public static string FileFingerprint(string path)
    {
        var info = new FileInfo(path);
        return info.Exists
            ? $"{info.FullName}|{info.Length}|{info.LastWriteTimeUtc.Ticks}"
            : $"{info.FullName}|missing";
    }

    private string GetCachePath(string key) => Path.Combine(_cacheDir, $"{key}.json");
}
