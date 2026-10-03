namespace GloSharp.Core;

/// <summary>
/// The marker-only parts of a result, which need no compilation: highlights
/// (<c>@highlight</c>/<c>@focus</c>/<c>@diff</c>), custom tags (<c>@log</c>, ...) and hidden
/// ranges (cuts, regions).
/// </summary>
internal static class AnnotationExtractor
{
    public static List<GloSharpHighlight> GetHighlights(Snippet snippet)
    {
        var processedLines = snippet.Markers.ProcessedCode.Split('\n');
        return snippet.Markers.Highlights
            .Where(h => h.TargetOriginalLine >= 0 && h.TargetOriginalLine < processedLines.Length)
            .Select(h => new GloSharpHighlight
            {
                Line = h.TargetOriginalLine,
                Character = 0,
                Length = processedLines[h.TargetOriginalLine].Length,
                Kind = h.Kind,
            })
            .ToList();
    }

    public static List<GloSharpTag> GetTags(Snippet snippet)
    {
        var processedLineCount = snippet.Markers.ProcessedCode.Split('\n').Length;
        return snippet.Markers.Tags
            .Where(t => t.TargetOriginalLine >= 0 && t.TargetOriginalLine < processedLineCount)
            .Select(t => new GloSharpTag
            {
                Name = t.Name,
                Text = t.Text,
                Line = t.TargetOriginalLine,
            })
            .ToList();
    }

    public static List<GloSharpHiddenRange> GetHiddenRanges(Snippet snippet)
    {
        var markers = snippet.Markers;
        return markers.HiddenRanges
            .Select(r => new GloSharpHiddenRange
            {
                Line = CountVisibleLinesBefore(markers.LineMap, r.StartLine),
                SourceStartLine = snippet.ToSourceLine(r.StartLine),
                SourceEndLine = snippet.ToSourceLine(r.EndLine),
            })
            .ToList();
    }

    /// <summary>The number of processed lines whose input line is before <paramref name="inputLine"/>.</summary>
    private static int CountVisibleLinesBefore(int[] lineMap, int inputLine)
    {
        var index = Array.BinarySearch(lineMap, inputLine);
        return index >= 0 ? index : ~index;
    }
}
