using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <summary>
/// Recovers NuGet package identities from the reference paths recorded in a complog or
/// .glocontext, for <c>meta.packages</c>. References under a NuGet package folder look like
/// <c>.../&lt;id&gt;/&lt;version&gt;/(lib|ref|runtimes)/...</c>. Targeting packs are skipped.
/// </summary>
internal static partial class ComplogPackageInference
{
    private static readonly HashSet<string> AssetFolders = new(StringComparer.OrdinalIgnoreCase) { "lib", "ref", "runtimes", "build" };

    [GeneratedRegex(@"^\d+(\.\d+){1,3}(-[0-9A-Za-z.\-]+)?(\+[0-9A-Za-z.\-]+)?$")]
    private static partial Regex VersionPattern();

    public static List<PackageReference> InferPackages(IEnumerable<MetadataReference> references)
    {
        var packages = new Dictionary<string, PackageReference>(StringComparer.OrdinalIgnoreCase);

        foreach (var reference in references)
        {
            var path = (reference as PortableExecutableReference)?.FilePath ?? reference.Display;
            if (string.IsNullOrEmpty(path)) continue;

            var segments = path.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            for (var i = 0; i + 2 < segments.Length; i++)
            {
                if (!AssetFolders.Contains(segments[i + 2]) || !VersionPattern().IsMatch(segments[i + 1]))
                    continue;

                var id = segments[i];
                var version = segments[i + 1];

                // Targeting packs (Microsoft.NETCore.App.Ref, ...) and SDK packs are not packages
                if (id.EndsWith(".Ref", StringComparison.OrdinalIgnoreCase)
                    || (i > 0 && segments[i - 1].Equals("packs", StringComparison.OrdinalIgnoreCase)))
                    break;

                if (!packages.ContainsKey(id))
                {
                    var packageDir = string.Join('/', segments.Take(i + 2));
                    if (path.StartsWith('/')) packageDir = "/" + packageDir;
                    packages[id] = new PackageReference
                    {
                        Name = ReadNuspecId(packageDir, id) ?? id,
                        Version = version,
                    };
                }
                break;
            }
        }

        return packages.Values.OrderBy(p => p.Name, StringComparer.OrdinalIgnoreCase).ToList();
    }

    /// <summary>
    /// True when the references include ASP.NET Core (the Web SDK's shared framework), in which
    /// case the Web SDK's implicit usings apply.
    /// </summary>
    public static bool ReferencesAspNetCore(IEnumerable<MetadataReference> references) =>
        references.Any(r =>
        {
            var path = (r as PortableExecutableReference)?.FilePath ?? r.Display;
            return path != null && Path.GetFileName(path.Replace('\\', '/'))
                .Equals("Microsoft.AspNetCore.dll", StringComparison.OrdinalIgnoreCase);
        });

    /// <summary>
    /// The global packages folder lower-cases ids; the nuspec next to the package (when present
    /// on this machine) has the canonical casing.
    /// </summary>
    private static string? ReadNuspecId(string packageDir, string id)
    {
        try
        {
            var nuspec = Path.Combine(packageDir, id.ToLowerInvariant() + ".nuspec");
            if (!File.Exists(nuspec)) return null;
            var doc = XDocument.Load(nuspec);
            return doc.Descendants().FirstOrDefault(e => e.Name.LocalName == "id")?.Value.Trim();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Xml.XmlException)
        {
            return null;
        }
    }
}
