using System.Text.RegularExpressions;

namespace GloSharp.Core;

public static partial class RegionExtractor
{
    [GeneratedRegex(@"^\s*#region(?:\s+(.*?))?\s*$")]
    private static partial Regex RegionStartPattern();

    [GeneratedRegex(@"^\s*#endregion\b")]
    private static partial Regex RegionEndPattern();

    /// <summary>
    /// Finds a named #region in the source and returns the line indices of its
    /// <c>#region</c> and matching <c>#endregion</c> lines. Nested regions are matched by
    /// depth, so an inner <c>#endregion</c> does not close the outer region. If the region is
    /// never closed, the end of the file is treated as its end.
    /// </summary>
    public static (int StartLine, int EndLine) FindRegion(string source, string regionName)
    {
        var lines = source.Split('\n');
        var regionStart = -1;
        var depth = 0;

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i].TrimEnd('\r');

            var start = RegionStartPattern().Match(line);
            if (start.Success)
            {
                if (regionStart < 0)
                {
                    if (start.Groups[1].Value.Trim() == regionName)
                    {
                        regionStart = i;
                        depth = 1;
                    }
                }
                else
                {
                    depth++;
                }
                continue;
            }

            if (regionStart >= 0 && RegionEndPattern().IsMatch(line))
            {
                depth--;
                if (depth == 0)
                    return (regionStart, i);
            }
        }

        if (regionStart >= 0)
            return (regionStart, lines.Length - 1);

        throw new InvalidOperationException($"Region '{regionName}' not found in source file.");
    }

    /// <summary>
    /// Returns a per-line mask (indexed like <c>source.Split('\n')</c>) of lines to hide when
    /// rendering only the named region: everything outside the region, the region's own
    /// <c>#region</c>/<c>#endregion</c> lines and any nested region directives inside it.
    /// Hidden lines are still compiled, so the region sees the rest of the file and the
    /// region directives stay balanced.
    /// </summary>
    public static bool[] GetHiddenLineMask(string source, string regionName)
    {
        var (startLine, endLine) = FindRegion(source, regionName);
        var lines = source.Split('\n');
        var hidden = new bool[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            if (i <= startLine || i >= endLine)
            {
                hidden[i] = true;
                continue;
            }

            var line = lines[i].TrimEnd('\r');
            if (RegionStartPattern().IsMatch(line) || RegionEndPattern().IsMatch(line))
                hidden[i] = true;
        }

        // An unclosed region runs to the end of the file: keep its last line visible
        // unless it is itself the #region line.
        var lastLine = lines[endLine].TrimEnd('\r');
        if (endLine == lines.Length - 1 && endLine != startLine
            && !RegionEndPattern().IsMatch(lastLine) && !RegionStartPattern().IsMatch(lastLine))
        {
            hidden[endLine] = false;
        }

        return hidden;
    }
}
