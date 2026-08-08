using System.Security.Cryptography;
using System.Text;

namespace GloSharp.Core;

/// <summary>
/// Deterministic content hash over a targeting pack's <c>ref/**/*.dll</c> files.
/// This is the single verification hash recorded per pack in a v2 manifest —
/// per-file hashes would be redundant (every pointer targets the same verified pack)
/// and their entropy dominated the compressed manifest size.
/// </summary>
internal static class PackContentHasher
{
    /// <summary>
    /// Hash definition: SHA-256 over, for each ref DLL sorted by forward-slash relative
    /// path (ordinal): UTF-8(relativePath) + 0x00 + SHA-256(file bytes).
    /// Returns the hash and the file contents keyed by relative path.
    /// </summary>
    public static (string ContentHash, Dictionary<string, byte[]> Files) HashRefDlls(string packRoot)
    {
        var files = new Dictionary<string, byte[]>(StringComparer.Ordinal);
        var refRoot = Path.Combine(packRoot, "ref");
        if (Directory.Exists(refRoot))
        {
            var relativePaths = Directory.EnumerateFiles(refRoot, "*.dll", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(packRoot, f).Replace('\\', '/'))
                .OrderBy(p => p, StringComparer.Ordinal);
            foreach (var relative in relativePaths)
            {
                files[relative] = File.ReadAllBytes(
                    Path.Combine(packRoot, relative.Replace('/', Path.DirectorySeparatorChar)));
            }
        }

        return (ComputeHash(files), files);
    }

    /// <summary>
    /// The expansion set of a whole-pack (`packAll`) reference: the DLLs that are
    /// direct children of <c>ref/&lt;tfm&gt;/</c>, sorted by relative path ordinal.
    /// The compactor's collapse check and the resolver's expansion both use this,
    /// so the two can never disagree about what "the whole pack" means.
    /// </summary>
    public static List<string> DirectRefDlls(IEnumerable<string> relativePaths, string tfm)
    {
        var prefix = $"ref/{tfm}/";
        return relativePaths
            .Where(p => p.StartsWith(prefix, StringComparison.Ordinal)
                && p.IndexOf('/', prefix.Length) < 0
                && p.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            .OrderBy(p => p, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The tfm directory of a path that is exactly <c>ref/&lt;tfm&gt;/&lt;file&gt;.dll</c>,
    /// or null for anything else (nested files, non-DLLs, non-ref paths).
    /// </summary>
    public static string? TfmOfDirectRefDll(string path)
    {
        var segments = path.Split('/');
        if (segments.Length != 3 || segments[0] != "ref" ||
            !segments[2].EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            return null;
        return segments[1];
    }

    private static string ComputeHash(Dictionary<string, byte[]> files)
    {
        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        Span<byte> separator = stackalloc byte[1];
        foreach (var relative in files.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(separator);
            hash.AppendData(SHA256.HashData(files[relative]));
        }
        return Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant();
    }
}
