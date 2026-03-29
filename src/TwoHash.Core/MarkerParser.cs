using System.Text.RegularExpressions;

namespace TwoHash.Core;

public record HoverQuery(int OriginalLine, int Column);
public record CompletionQuery(int OriginalLine, int Column);
public record ErrorExpectation(int OriginalLine, List<string> Codes);
public record HighlightDirective(string Kind, int TargetOriginalLine);

public class MarkerParseResult
{
    public required string ProcessedCode { get; init; }
    public required string OriginalCode { get; init; }
    public required List<HoverQuery> HoverQueries { get; init; }
    public required List<CompletionQuery> CompletionQueries { get; init; }
    public required List<ErrorExpectation> ErrorExpectations { get; init; }
    public required bool NoErrors { get; init; }
    public required bool SuppressAllErrors { get; init; }
    public required List<string> SuppressedErrorCodes { get; init; }
    public required List<HiddenRange> HiddenRanges { get; init; }
    public required List<HighlightDirective> Highlights { get; init; }
    public required int[] LineMap { get; init; } // processedLine -> originalLine
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
}

public record HiddenRange(int StartLine, int EndLine);

public static partial class MarkerParser
{
    private static readonly Regex HoverMarkerRegex = HoverMarkerPattern();
    private static readonly Regex CompletionMarkerRegex = CompletionMarkerPattern();
    private static readonly Regex ErrorsDirectiveRegex = ErrorsDirectivePattern();
    private static readonly Regex NoErrorsDirectiveRegex = NoErrorsDirectivePattern();
    private static readonly Regex SuppressErrorsDirectiveRegex = SuppressErrorsDirectivePattern();
    private static readonly Regex CutMarkerRegex = CutMarkerPattern();
    private static readonly Regex HideDirectiveRegex = HideDirectivePattern();
    private static readonly Regex ShowDirectiveRegex = ShowDirectivePattern();
    private static readonly Regex HighlightDirectiveRegex = HighlightDirectivePattern();
    private static readonly Regex FocusDirectiveRegex = FocusDirectivePattern();
    private static readonly Regex DiffDirectiveRegex = DiffDirectivePattern();
    private static readonly Regex LangVersionDirectiveRegex = LangVersionDirectivePattern();
    private static readonly Regex NullableDirectiveRegex = NullableDirectivePattern();

    [GeneratedRegex(@"^(\s*)//\s*\^(\?)")]
    private static partial Regex HoverMarkerPattern();

    [GeneratedRegex(@"^(\s*)//\s*\^(\|)")]
    private static partial Regex CompletionMarkerPattern();

    [GeneratedRegex(@"^\s*//\s*@errors:\s*(.+)$")]
    private static partial Regex ErrorsDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@noErrors\s*$")]
    private static partial Regex NoErrorsDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@suppressErrors(?::\s*(.+))?\s*$")]
    private static partial Regex SuppressErrorsDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*---cut---\s*$")]
    private static partial Regex CutMarkerPattern();

    [GeneratedRegex(@"^\s*//\s*@hide\s*$")]
    private static partial Regex HideDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@show\s*$")]
    private static partial Regex ShowDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@highlight(?::\s*(.+))?\s*$")]
    private static partial Regex HighlightDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@focus(?::\s*(.+))?\s*$")]
    private static partial Regex FocusDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@diff:\s*([+-])\s*$")]
    private static partial Regex DiffDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@langVersion:\s*(.+?)\s*$")]
    private static partial Regex LangVersionDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*@nullable:\s*(.+?)\s*$")]
    private static partial Regex NullableDirectivePattern();

    public static MarkerParseResult Parse(string source)
    {
        var lines = source.Split('\n');
        var hoverQueries = new List<HoverQuery>();
        var completionQueries = new List<CompletionQuery>();
        var errorExpectations = new List<ErrorExpectation>();
        var hiddenRanges = new List<HiddenRange>();
        var highlights = new List<HighlightDirective>();
        var noErrors = false;
        var suppressAllErrors = false;
        var suppressedErrorCodes = new List<string>();
        string? langVersion = null;
        string? nullable = null;

        // Track range-based directives to resolve after line mapping
        var rangeDirectives = new List<(string Kind, int StartLine, int EndLine)>(); // 1-based output lines

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
                var targetLine = FindNextCodeLine(lines, isMarkerLine, i);
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

            // @suppressErrors or @suppressErrors: CS0246, CS0103
            var suppressMatch = SuppressErrorsDirectiveRegex.Match(line);
            if (suppressMatch.Success)
            {
                var codesArg = suppressMatch.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(codesArg))
                {
                    suppressAllErrors = true;
                }
                else
                {
                    var codes = codesArg
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList();
                    suppressedErrorCodes.AddRange(codes);
                }
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

            // @langVersion: <value>
            var langVersionMatch = LangVersionDirectiveRegex.Match(line);
            if (langVersionMatch.Success)
            {
                langVersion = langVersionMatch.Groups[1].Value.Trim().ToLowerInvariant();
                isMarkerLine[i] = true;
                continue;
            }

            // @nullable: <value>
            var nullableMatch = NullableDirectiveRegex.Match(line);
            if (nullableMatch.Success)
            {
                nullable = nullableMatch.Groups[1].Value.Trim().ToLowerInvariant();
                isMarkerLine[i] = true;
                continue;
            }

            // @highlight or @highlight: N or @highlight: N-M
            var highlightMatch = HighlightDirectiveRegex.Match(line);
            if (highlightMatch.Success)
            {
                var arg = highlightMatch.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(arg))
                {
                    // Bare @highlight — targets next code line
                    var targetLine = FindNextCodeLine(lines, isMarkerLine, i);
                    if (targetLine >= 0)
                        highlights.Add(new HighlightDirective("highlight", targetLine));
                }
                else
                {
                    // Range argument — store for resolution after line mapping
                    ParseLineRange(arg, "highlight", rangeDirectives);
                }
                isMarkerLine[i] = true;
                continue;
            }

            // @focus or @focus: N or @focus: N-M
            var focusMatch = FocusDirectiveRegex.Match(line);
            if (focusMatch.Success)
            {
                var arg = focusMatch.Groups[1].Value.Trim();
                if (string.IsNullOrEmpty(arg))
                {
                    var targetLine = FindNextCodeLine(lines, isMarkerLine, i);
                    if (targetLine >= 0)
                        highlights.Add(new HighlightDirective("focus", targetLine));
                }
                else
                {
                    ParseLineRange(arg, "focus", rangeDirectives);
                }
                isMarkerLine[i] = true;
                continue;
            }

            // @diff: + or @diff: -
            var diffMatch = DiffDirectiveRegex.Match(line);
            if (diffMatch.Success)
            {
                var sign = diffMatch.Groups[1].Value;
                var kind = sign == "+" ? "add" : "remove";
                var targetLine = FindNextCodeLine(lines, isMarkerLine, i);
                if (targetLine >= 0)
                    highlights.Add(new HighlightDirective(kind, targetLine));
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

        // Resolve range-based directives (1-based output line numbers → 0-based processed lines)
        foreach (var (kind, startLine, endLine) in rangeDirectives)
        {
            for (var lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var processedLine = lineNum - 1; // 1-based → 0-based
                if (processedLine >= 0 && processedLine < processedLines.Count)
                {
                    // Store as a HighlightDirective with the original line from lineMap
                    // so it gets remapped correctly below
                    highlights.Add(new HighlightDirective(kind, lineMap[processedLine]));
                }
            }
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

        // Remap highlight directives
        var remappedHighlights = new List<HighlightDirective>();
        foreach (var hl in highlights)
        {
            var processedLine = lineMap.IndexOf(hl.TargetOriginalLine);
            if (processedLine >= 0)
            {
                remappedHighlights.Add(new HighlightDirective(hl.Kind, processedLine));
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
            SuppressAllErrors = suppressAllErrors,
            SuppressedErrorCodes = suppressedErrorCodes,
            HiddenRanges = hiddenRanges,
            Highlights = remappedHighlights,
            LineMap = lineMap.ToArray(),
            LangVersion = langVersion,
            Nullable = nullable,
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
                SuppressErrorsDirectiveRegex.IsMatch(line) ||
                CutMarkerRegex.IsMatch(line) ||
                HideDirectiveRegex.IsMatch(line) ||
                ShowDirectiveRegex.IsMatch(line) ||
                HighlightDirectiveRegex.IsMatch(line) ||
                FocusDirectiveRegex.IsMatch(line) ||
                DiffDirectiveRegex.IsMatch(line) ||
                LangVersionDirectiveRegex.IsMatch(line) ||
                NullableDirectiveRegex.IsMatch(line))
            {
                continue;
            }
            compilationLines.Add(line);
        }

        return string.Join('\n', compilationLines);
    }

    private static void ParseLineRange(string arg, string kind, List<(string Kind, int StartLine, int EndLine)> rangeDirectives)
    {
        var dashIndex = arg.IndexOf('-');
        if (dashIndex >= 0)
        {
            if (int.TryParse(arg[..dashIndex], out var start) && int.TryParse(arg[(dashIndex + 1)..], out var end))
            {
                rangeDirectives.Add((kind, start, end));
            }
        }
        else if (int.TryParse(arg, out var single))
        {
            rangeDirectives.Add((kind, single, single));
        }
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

    private static int FindNextCodeLine(string[] lines, bool[] isMarkerLine, int fromLine)
    {
        for (var i = fromLine + 1; i < lines.Length; i++)
        {
            if (isMarkerLine[i])
                continue;

            var line = lines[i];
            if (!HoverMarkerRegex.IsMatch(line) &&
                !CompletionMarkerRegex.IsMatch(line) &&
                !ErrorsDirectiveRegex.IsMatch(line) &&
                !NoErrorsDirectiveRegex.IsMatch(line) &&
                !SuppressErrorsDirectiveRegex.IsMatch(line) &&
                !CutMarkerRegex.IsMatch(line) &&
                !HideDirectiveRegex.IsMatch(line) &&
                !ShowDirectiveRegex.IsMatch(line) &&
                !HighlightDirectiveRegex.IsMatch(line) &&
                !FocusDirectiveRegex.IsMatch(line) &&
                !DiffDirectiveRegex.IsMatch(line) &&
                !LangVersionDirectiveRegex.IsMatch(line) &&
                !NullableDirectiveRegex.IsMatch(line))
            {
                return i;
            }
        }
        return -1;
    }
}
