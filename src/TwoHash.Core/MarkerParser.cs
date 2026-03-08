using System.Text.RegularExpressions;

namespace TwoHash.Core;

public record HoverQuery(int OriginalLine, int Column);
public record CompletionQuery(int OriginalLine, int Column);
public record ErrorExpectation(int OriginalLine, List<string> Codes);

public class MarkerParseResult
{
    public required string ProcessedCode { get; init; }
    public required string OriginalCode { get; init; }
    public required List<HoverQuery> HoverQueries { get; init; }
    public required List<CompletionQuery> CompletionQueries { get; init; }
    public required List<ErrorExpectation> ErrorExpectations { get; init; }
    public required bool NoErrors { get; init; }
    public required List<HiddenRange> HiddenRanges { get; init; }
    public required int[] LineMap { get; init; } // processedLine -> originalLine
}

public record HiddenRange(int StartLine, int EndLine);

public static partial class MarkerParser
{
    private static readonly Regex HoverMarkerRegex = HoverMarkerPattern();
    private static readonly Regex CompletionMarkerRegex = CompletionMarkerPattern();
    private static readonly Regex ErrorsDirectiveRegex = ErrorsDirectivePattern();
    private static readonly Regex NoErrorsDirectiveRegex = NoErrorsDirectivePattern();
    private static readonly Regex CutMarkerRegex = CutMarkerPattern();
    private static readonly Regex HideDirectiveRegex = HideDirectivePattern();
    private static readonly Regex ShowDirectiveRegex = ShowDirectivePattern();

    [GeneratedRegex(@"^(\s*)//\s*\^(\?)")]
    private static partial Regex HoverMarkerPattern();

    [GeneratedRegex(@"^(\s*)//\s*\^(\|)")]
    private static partial Regex CompletionMarkerPattern();

    [GeneratedRegex(@"^\s*//\s*@errors:\s*(.+)$")]
    private static partial Regex ErrorsDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@noErrors\s*$")]
    private static partial Regex NoErrorsDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*---cut---\s*$")]
    private static partial Regex CutMarkerPattern();

    [GeneratedRegex(@"^\s*//\s*@hide\s*$")]
    private static partial Regex HideDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@show\s*$")]
    private static partial Regex ShowDirectivePattern();

    public static MarkerParseResult Parse(string source)
    {
        var lines = source.Split('\n');
        var hoverQueries = new List<HoverQuery>();
        var completionQueries = new List<CompletionQuery>();
        var errorExpectations = new List<ErrorExpectation>();
        var hiddenRanges = new List<HiddenRange>();
        var noErrors = false;

        // First pass: identify marker lines and collect metadata
        var isMarkerLine = new bool[lines.Length];
        var isCutLine = -1;
        var hideStart = -1;
        var isHiddenLine = new bool[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];

            // ^? hover query
            var hoverMatch = HoverMarkerRegex.Match(line);
            if (hoverMatch.Success)
            {
                var caretCol = line.IndexOf('^');
                if (caretCol >= 0)
                {
                    // The hover targets the preceding code line
                    // We need to find the preceding non-marker line
                    var targetOriginalLine = FindPrecedingCodeLine(lines, isMarkerLine, i);
                    if (targetOriginalLine >= 0)
                    {
                        hoverQueries.Add(new HoverQuery(targetOriginalLine, caretCol));
                    }
                }
                isMarkerLine[i] = true;
                continue;
            }

            // ^| completion query
            var completionMatch = CompletionMarkerRegex.Match(line);
            if (completionMatch.Success)
            {
                var caretCol = line.IndexOf('^');
                if (caretCol >= 0)
                {
                    var targetOriginalLine = FindPrecedingCodeLine(lines, isMarkerLine, i);
                    if (targetOriginalLine >= 0)
                    {
                        completionQueries.Add(new CompletionQuery(targetOriginalLine, caretCol));
                    }
                }
                isMarkerLine[i] = true;
                continue;
            }

            // @errors: CS1002, CS0246
            var errorsMatch = ErrorsDirectiveRegex.Match(line);
            if (errorsMatch.Success)
            {
                var codes = errorsMatch.Groups[1].Value
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .ToList();
                // Error expectations apply to the next code line
                var targetLine = FindNextCodeLine(lines, i);
                if (targetLine >= 0)
                {
                    errorExpectations.Add(new ErrorExpectation(targetLine, codes));
                }
                isMarkerLine[i] = true;
                continue;
            }

            // @noErrors
            if (NoErrorsDirectiveRegex.IsMatch(line))
            {
                noErrors = true;
                isMarkerLine[i] = true;
                continue;
            }

            // ---cut---
            if (CutMarkerRegex.IsMatch(line))
            {
                isCutLine = i;
                isMarkerLine[i] = true;
                continue;
            }

            // @hide
            if (HideDirectiveRegex.IsMatch(line))
            {
                hideStart = i;
                isMarkerLine[i] = true;
                continue;
            }

            // @show
            if (ShowDirectiveRegex.IsMatch(line))
            {
                if (hideStart >= 0)
                {
                    for (var h = hideStart; h <= i; h++)
                        isHiddenLine[h] = true;
                    hiddenRanges.Add(new HiddenRange(hideStart, i));
                    hideStart = -1;
                }
                isMarkerLine[i] = true;
                continue;
            }
        }

        // If @hide was never closed, hide to end of file
        if (hideStart >= 0)
        {
            for (var h = hideStart; h < lines.Length; h++)
                isHiddenLine[h] = true;
            hiddenRanges.Add(new HiddenRange(hideStart, lines.Length - 1));
        }

        // If there's a cut marker, everything before it is hidden
        if (isCutLine >= 0)
        {
            for (var h = 0; h <= isCutLine; h++)
                isHiddenLine[h] = true;
            hiddenRanges.Insert(0, new HiddenRange(0, isCutLine));
        }

        // Build processed code: remove marker lines and hidden lines
        var processedLines = new List<string>();
        var lineMap = new List<int>(); // processedLine -> originalLine

        for (var i = 0; i < lines.Length; i++)
        {
            if (isMarkerLine[i] || isHiddenLine[i])
                continue;

            processedLines.Add(lines[i]);
            lineMap.Add(i);
        }

        // Remap hover queries from original line to processed line
        var remappedQueries = new List<HoverQuery>();
        foreach (var query in hoverQueries)
        {
            var processedLine = lineMap.IndexOf(query.OriginalLine);
            if (processedLine >= 0)
            {
                remappedQueries.Add(new HoverQuery(processedLine, query.Column));
            }
        }

        // Remap completion queries
        var remappedCompletions = new List<CompletionQuery>();
        foreach (var query in completionQueries)
        {
            var processedLine = lineMap.IndexOf(query.OriginalLine);
            if (processedLine >= 0)
            {
                remappedCompletions.Add(new CompletionQuery(processedLine, query.Column));
            }
        }

        // Remap error expectations
        var remappedErrors = new List<ErrorExpectation>();
        foreach (var err in errorExpectations)
        {
            var processedLine = lineMap.IndexOf(err.OriginalLine);
            if (processedLine >= 0)
            {
                remappedErrors.Add(new ErrorExpectation(processedLine, err.Codes));
            }
        }

        return new MarkerParseResult
        {
            ProcessedCode = string.Join('\n', processedLines),
            OriginalCode = source,
            HoverQueries = remappedQueries,
            CompletionQueries = remappedCompletions,
            ErrorExpectations = remappedErrors,
            NoErrors = noErrors,
            HiddenRanges = hiddenRanges,
            LineMap = lineMap.ToArray(),
        };
    }

    /// <summary>
    /// For compilation, we need all lines including hidden ones but without marker lines.
    /// </summary>
    public static string GetCompilationCode(string source)
    {
        var lines = source.Split('\n');
        var compilationLines = new List<string>();

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            if (HoverMarkerRegex.IsMatch(line) ||
                CompletionMarkerRegex.IsMatch(line) ||
                ErrorsDirectiveRegex.IsMatch(line) ||
                NoErrorsDirectiveRegex.IsMatch(line) ||
                CutMarkerRegex.IsMatch(line) ||
                HideDirectiveRegex.IsMatch(line) ||
                ShowDirectiveRegex.IsMatch(line))
            {
                continue;
            }
            compilationLines.Add(line);
        }

        return string.Join('\n', compilationLines);
    }

    private static int FindPrecedingCodeLine(string[] lines, bool[] isMarkerLine, int fromLine)
    {
        for (var i = fromLine - 1; i >= 0; i--)
        {
            if (!isMarkerLine[i])
                return i;
        }
        return -1;
    }

    private static int FindNextCodeLine(string[] lines, int fromLine)
    {
        for (var i = fromLine + 1; i < lines.Length; i++)
        {
            var line = lines[i];
            if (!HoverMarkerRegex.IsMatch(line) &&
                !CompletionMarkerRegex.IsMatch(line) &&
                !ErrorsDirectiveRegex.IsMatch(line) &&
                !NoErrorsDirectiveRegex.IsMatch(line) &&
                !CutMarkerRegex.IsMatch(line) &&
                !HideDirectiveRegex.IsMatch(line) &&
                !ShowDirectiveRegex.IsMatch(line))
            {
                return i;
            }
        }
        return -1;
    }
}
