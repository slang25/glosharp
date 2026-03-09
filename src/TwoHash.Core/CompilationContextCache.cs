using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.CodeAnalysis;

namespace TwoHash.Core;

public class CompilationContextCache
{
    private readonly Dictionary<string, List<MetadataReference>> _cache = new();

    public List<MetadataReference> GetOrAdd(string key, Func<List<MetadataReference>> factory)
    {
        if (_cache.TryGetValue(key, out var cached))
            return cached;

        var references = factory();
        _cache[key] = references;
        return references;
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
