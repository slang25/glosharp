namespace GloSharp.Core;

/// <summary>
/// A snippet after text-only preprocessing: <c>#:</c> directives stripped, the region mask
/// applied and markers parsed. Immutable, and the single source of the line maps every later
/// stage uses.
/// </summary>
/// <remarks>
/// Line spaces (all 0-based):
/// <list type="bullet">
/// <item><b>source</b>: the text the caller passed in (what <c>sourceLine</c> fields report).</item>
/// <item><b>input</b>: the text handed to <see cref="MarkerParser"/> (source minus <c>#:</c> lines).</item>
/// <item><b>compilation</b>: <see cref="MarkerParseResult.CompilationCode"/>, input minus marker lines.</item>
/// <item><b>processed</b>: <see cref="MarkerParseResult.ProcessedCode"/>, what is rendered.</item>
/// </list>
/// </remarks>
internal sealed class Snippet
{
    private Snippet(string source, FileDirectiveResult directives, MarkerParseResult markers)
    {
        Source = source;
        Directives = directives;
        Markers = markers;
        CompilationLines = new LineIndex(markers.CompilationCode);
    }

    /// <summary>The text as the caller passed it.</summary>
    public string Source { get; }

    public FileDirectiveResult Directives { get; }

    public MarkerParseResult Markers { get; }

    /// <summary>Offset/line lookups over <see cref="MarkerParseResult.CompilationCode"/>.</summary>
    public LineIndex CompilationLines { get; }

    /// <summary>The <c>#:sdk</c> directive, if any.</summary>
    public string? Sdk => Directives.GetSdk();

    /// <summary>
    /// Text-only preprocessing (no Roslyn binding): strips <c>#:</c> directives, hides everything
    /// outside <paramref name="regionName"/> (it is still compiled) and parses markers.
    /// </summary>
    /// <exception cref="InvalidOperationException">The region does not exist.</exception>
    public static Snippet Prepare(string source, string? regionName)
    {
        // #: file-based app directives are stripped first
        var directives = FileDirectiveParser.Parse(source);
        var text = directives.CleanedSource;

        // Region extraction is a hidden-line mask, so the rest of the file still compiles
        var regionMask = regionName != null
            ? RegionExtractor.GetHiddenLineMask(text, regionName)
            : null;

        return new Snippet(source, directives, MarkerParser.Parse(text, regionMask));
    }

    /// <summary>Maps an input line to the line in the original source text.</summary>
    public int ToSourceLine(int inputLine) =>
        inputLine >= 0 && inputLine < Directives.LineMap.Length ? Directives.LineMap[inputLine] : inputLine;

    /// <summary>Maps a processed (rendered) line to the line in the original source text.</summary>
    public int ProcessedToSourceLine(int processedLine) => ToSourceLine(Markers.LineMap[processedLine]);

    /// <summary>Maps a processed (rendered) line to its compilation line.</summary>
    public int ProcessedToCompilationLine(int processedLine) =>
        Markers.InputLineToCompilation[Markers.LineMap[processedLine]];

    /// <summary>Maps a compilation line to its input line.</summary>
    public int CompilationToInputLine(int compilationLine) => Markers.CompilationLineMap[compilationLine];

    /// <summary>Maps a compilation line to its processed line, or -1 when the line is hidden.</summary>
    public int CompilationToProcessedLine(int compilationLine) =>
        Markers.InputLineToProcessed[Markers.CompilationLineMap[compilationLine]];
}
