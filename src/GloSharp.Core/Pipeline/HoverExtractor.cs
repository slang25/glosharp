using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>
/// Finds the hovers of a snippet: persistent ones pinned by <c>^?</c> markers, then an automatic
/// hover for every other hoverable token in the visible code. Persistent hovers come first.
/// </summary>
internal static class HoverExtractor
{
    /// <param name="model">The semantic model of the snippet's own syntax tree.</param>
    public static List<GloSharpHover> Extract(Snippet snippet, SemanticModel model, List<string> warnings)
    {
        var persistentHovers = ExtractPersistent(snippet, model.SyntaxTree, model, warnings);
        var autoHovers = ExtractAuto(snippet, model.SyntaxTree, model, persistentHovers);

        var merged = new List<GloSharpHover>(persistentHovers.Count + autoHovers.Count);
        merged.AddRange(persistentHovers);
        merged.AddRange(autoHovers);
        return merged;
    }

    private static List<GloSharpHover> ExtractPersistent(
        Snippet snippet,
        SyntaxTree tree,
        SemanticModel model,
        List<string> warnings)
    {
        var lines = snippet.CompilationLines;
        var hovers = new List<GloSharpHover>();
        var root = tree.GetCompilationUnitRoot();

        foreach (var query in snippet.Markers.HoverQueries)
        {
            var sourceLine = snippet.ProcessedToSourceLine(query.OriginalLine);
            var markerLine = snippet.ToSourceLine(query.MarkerInputLine);
            var compLine = snippet.ProcessedToCompilationLine(query.OriginalLine);

            if (query.Column >= lines.GetLineContentLength(compLine))
            {
                warnings.Add(lines.GetLineContentLength(compLine) == 0
                    ? $"Line {markerLine + 1}: the ^? marker points at line {sourceLine + 1}, which is empty; the hover was skipped."
                    : $"Line {markerLine + 1}: the ^? marker points at column {query.Column + 1}, past the end of line {sourceLine + 1}; the hover was skipped.");
                continue;
            }

            var position = lines.GetLineStart(compLine) + query.Column;
            var token = root.FindToken(position);
            GloSharpHover? hover = null;
            if (token.Span.Contains(position))
            {
                var character = token.SpanStart - lines.GetLineStart(lines.GetLine(token.SpanStart));
                hover = HoverBuilder.Build(token, model, query.OriginalLine, character, persistent: true);
            }

            if (hover == null)
            {
                warnings.Add(
                    $"Line {markerLine + 1}: the ^? marker (line {sourceLine + 1}, column {query.Column + 1}) does not point at a symbol; the hover was skipped.");
                continue;
            }

            hovers.Add(hover);
        }

        return hovers;
    }

    private static List<GloSharpHover> ExtractAuto(
        Snippet snippet,
        SyntaxTree tree,
        SemanticModel model,
        List<GloSharpHover> persistentHovers)
    {
        var lines = snippet.CompilationLines;
        var hovers = new List<GloSharpHover>();
        var root = tree.GetCompilationUnitRoot();

        var persistentPositions = new HashSet<(int Line, int Character)>();
        foreach (var ph in persistentHovers)
            persistentPositions.Add((ph.Line, ph.Character));

        foreach (var token in root.DescendantTokens())
        {
            if (!HoverBuilder.IsHoverableToken(token))
                continue;

            var (compLine, character) = lines.GetPosition(token.SpanStart);
            var processedLine = snippet.CompilationToProcessedLine(compLine);
            if (processedLine < 0)
                continue; // hidden code

            if (persistentPositions.Contains((processedLine, character)))
                continue;

            var hover = HoverBuilder.Build(token, model, processedLine, character, persistent: false);
            if (hover != null)
                hovers.Add(hover);
        }

        return hovers;
    }
}
