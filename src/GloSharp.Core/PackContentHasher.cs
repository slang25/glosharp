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

        using var hash = IncrementalHash.CreateHash(HashAlgorithmName.SHA256);
        foreach (var relative in files.Keys.OrderBy(p => p, StringComparer.Ordinal))
        {
            hash.AppendData(Encoding.UTF8.GetBytes(relative));
            hash.AppendData(stackalloc byte[] { 0 });
            hash.AppendData(SHA256.HashData(files[relative]));
        }
        return (Convert.ToHexString(hash.GetHashAndReset()).ToLowerInvariant(), files);
    }
}
