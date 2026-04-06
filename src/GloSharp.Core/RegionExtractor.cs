using System.Text.RegularExpressions;

namespace GloSharp.Core;

public static partial class RegionExtractor
{
    [GeneratedRegex(@"^\s*#region\s+(.+)$")]
    private static partial Regex RegionStartPattern();

    [GeneratedRegex(@"^\s*#endregion")]
    private static partial Regex RegionEndPattern();

    /// <summary>
    /// Finds a named #region in the source and returns the start/end line indices
    /// (exclusive of the #region and #endregion lines themselves).
    /// </summary>
    public static (int StartLine, int EndLine) FindRegion(string source, string regionName)
    {
        var lines = source.Split('\n');
        var regionStart = -1;

        for (var i = 0; i < lines.Length; i++)
        {
            var match = RegionStartPattern().Match(lines[i]);
            if (match.Success && match.Groups[1].Value.Trim() == regionName)
            {
                regionStart = i;
                continue;
            }

            if (regionStart >= 0 && RegionEndPattern().IsMatch(lines[i]))
            {
                return (regionStart, i);
            }
        }

        if (regionStart >= 0)
        {
            // Region opened but never closed — treat end of file as end
            return (regionStart, lines.Length - 1);
        }

        throw new InvalidOperationException($"Region '{regionName}' not found in source file.");
    }

    /// <summary>
    /// Transforms the source so that everything outside the named region is hidden
    /// (using ---cut-start---/---cut-end--- markers), and the #region/#endregion lines themselves are removed.
    /// The full source is still available for compilation.
    /// </summary>
    public static string ApplyRegion(string source, string regionName)
    {
        var (regionStartLine, regionEndLine) = FindRegion(source, regionName);
        var lines = source.Split('\n');
        var result = new List<string>();

        // Hide everything before the region content
        if (regionStartLine > 0)
        {
            result.Add("// ---cut-start---");
            for (var i = 0; i < regionStartLine; i++)
                result.Add(lines[i]);
            result.Add("// ---cut-end---");
        }

        // Skip the #region line itself — it's a marker we don't include
        // Add the content lines inside the region
        for (var i = regionStartLine + 1; i < regionEndLine; i++)
        {
            result.Add(lines[i]);
        }

        // Hide everything after the region content (including #endregion)
        if (regionEndLine < lines.Length - 1)
        {
            result.Add("// ---cut-start---");
            for (var i = regionEndLine; i < lines.Length; i++)
                result.Add(lines[i]);
        }

        return string.Join('\n', result);
    }
}
