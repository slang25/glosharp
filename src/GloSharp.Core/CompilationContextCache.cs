using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <summary>
/// In-memory cache of resolved compilation context (references, complog resolutions) shared by
/// every snippet processed through one <see cref="GloSharpProcessor"/>. Thread-safe: concurrent
/// callers for the same key share a single factory invocation. A factory that throws is not
/// cached, so a later call can retry.
/// </summary>
public class CompilationContextCache
{
    private readonly ConcurrentDictionary<string, Lazy<object>> _cache = new(StringComparer.Ordinal);

    public List<MetadataReference> GetOrAdd(string key, Func<List<MetadataReference>> factory) =>
        GetOrAdd<List<MetadataReference>>(key, factory);

    public T GetOrAdd<T>(string key, Func<T> factory) where T : class
    {
        // Namespace keys by type so two kinds of value can never collide on one key
        var typedKey = typeof(T).FullName + "\0" + key;
        var lazy = _cache.GetOrAdd(typedKey,
            _ => new Lazy<object>(() => factory(), LazyThreadSafetyMode.ExecutionAndPublication));

        try
        {
            return (T)lazy.Value;
        }
        catch
        {
            _cache.TryRemove(new KeyValuePair<string, Lazy<object>>(typedKey, lazy));
            throw;
        }
    }

    public static string ComputeKey(
        string targetFramework,
        List<PackageReference>? packages,
        string? projectAssetsPath)
    {
        using var sha256 = SHA256.Create();
        var sb = new StringBuilder();
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

        if (projectAssetsPath != null && File.Exists(projectAssetsPath))
        {
            // Hash the file content for change detection
            var content = File.ReadAllBytes(projectAssetsPath);
            var hash = sha256.ComputeHash(content);
            sb.Append(Convert.ToHexString(hash).ToLowerInvariant());
        }
        else
        {
            sb.Append(projectAssetsPath ?? "");
        }

        var keyBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(sb.ToString()));
        return Convert.ToHexString(keyBytes).ToLowerInvariant();
    }
}
