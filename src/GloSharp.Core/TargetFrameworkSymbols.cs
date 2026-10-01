using System.Text.RegularExpressions;

namespace GloSharp.Core;

/// <summary>
/// Derives the preprocessor symbols the .NET SDK defines for a target framework moniker
/// (e.g. <c>net8.0</c> → <c>NET</c>, <c>NET8_0</c>, <c>NET8_0_OR_GREATER</c>, ...,
/// <c>NETCOREAPP</c>, <c>NETCOREAPP3_1_OR_GREATER</c>, ...), plus <c>TRACE</c>.
/// </summary>
/// <remarks>
/// Mirrors the SDK's <c>Microsoft.NET.Sdk.BeforeCommon.targets</c> logic. <c>DEBUG</c> is not
/// defined (snippets are not tied to a configuration).
/// </remarks>
public static partial class TargetFrameworkSymbols
{
    private static readonly string[] NetCoreAppVersions = ["1.0", "1.1", "2.0", "2.1", "2.2", "3.0", "3.1"];
    private static readonly string[] NetStandardVersions = ["1.0", "1.1", "1.2", "1.3", "1.4", "1.5", "1.6", "2.0", "2.1"];
    private static readonly string[] NetFrameworkVersions =
        ["20", "30", "35", "40", "45", "451", "452", "46", "461", "462", "47", "471", "472", "48", "481"];

    [GeneratedRegex(@"^net(?<ver>\d+\.\d+)(?:-(?<platform>[a-z]+)(?<platformVer>[\d.]*))?$", RegexOptions.IgnoreCase)]
    private static partial Regex ModernNetPattern();

    [GeneratedRegex(@"^netcoreapp(?<ver>\d+\.\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetCoreAppPattern();

    [GeneratedRegex(@"^netstandard(?<ver>\d+\.\d+)$", RegexOptions.IgnoreCase)]
    private static partial Regex NetStandardPattern();

    [GeneratedRegex(@"^net(?<ver>\d{2,3})$", RegexOptions.IgnoreCase)]
    private static partial Regex NetFrameworkPattern();

    public static IReadOnlyList<string> Get(string? targetFramework)
    {
        var symbols = new List<string> { "TRACE" };
        if (string.IsNullOrWhiteSpace(targetFramework))
            return symbols;

        var tfm = targetFramework.Trim();

        var modern = ModernNetPattern().Match(tfm);
        if (modern.Success && Version.TryParse(modern.Groups["ver"].Value, out var netVersion) && netVersion.Major >= 5)
        {
            symbols.Add("NETCOREAPP");
            foreach (var v in NetCoreAppVersions)
                symbols.Add($"NETCOREAPP{Ident(v)}_OR_GREATER");

            symbols.Add("NET");
            symbols.Add($"NET{netVersion.Major}_{netVersion.Minor}");
            for (var major = 5; major <= netVersion.Major; major++)
                symbols.Add($"NET{major}_0_OR_GREATER");

            if (modern.Groups["platform"].Success)
            {
                var platform = modern.Groups["platform"].Value.ToUpperInvariant();
                symbols.Add(platform);
                if (Version.TryParse(NormaliseVersion(modern.Groups["platformVer"].Value), out var platformVersion))
                {
                    symbols.Add($"{platform}{platformVersion.Major}_{Math.Max(0, platformVersion.Minor)}");
                }
            }

            return symbols;
        }

        var coreApp = NetCoreAppPattern().Match(tfm);
        if (coreApp.Success)
        {
            var ver = coreApp.Groups["ver"].Value;
            symbols.Add("NETCOREAPP");
            symbols.Add($"NETCOREAPP{Ident(ver)}");
            AddOrGreater(symbols, "NETCOREAPP", NetCoreAppVersions, ver);
            return symbols;
        }

        var netStandard = NetStandardPattern().Match(tfm);
        if (netStandard.Success)
        {
            var ver = netStandard.Groups["ver"].Value;
            symbols.Add("NETSTANDARD");
            symbols.Add($"NETSTANDARD{Ident(ver)}");
            AddOrGreater(symbols, "NETSTANDARD", NetStandardVersions, ver);
            return symbols;
        }

        var netFramework = NetFrameworkPattern().Match(tfm);
        if (netFramework.Success)
        {
            var ver = netFramework.Groups["ver"].Value;
            symbols.Add("NETFRAMEWORK");
            symbols.Add($"NET{ver}");
            AddOrGreater(symbols, "NET", NetFrameworkVersions, ver);
            return symbols;
        }

        return symbols;
    }

    private static void AddOrGreater(List<string> symbols, string prefix, string[] knownVersions, string version)
    {
        var index = Array.IndexOf(knownVersions, version);
        var last = index >= 0 ? index : knownVersions.Length - 1;
        for (var i = 0; i <= last; i++)
            symbols.Add($"{prefix}{Ident(knownVersions[i])}_OR_GREATER");
    }

    private static string Ident(string version) => version.Replace('.', '_');

    private static string NormaliseVersion(string version) =>
        string.IsNullOrEmpty(version) ? "" : version.Contains('.') ? version : version + ".0";
}
