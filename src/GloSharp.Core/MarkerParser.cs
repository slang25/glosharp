using System.Text.RegularExpressions;

namespace GloSharp.Core;

public record HoverQuery(int OriginalLine, int Column);
public record CompletionQuery(int OriginalLine, int Column);
public record ErrorExpectation(int OriginalLine, List<string> Codes);
public record HighlightDirective(string Kind, int TargetOriginalLine);
public record TagDirective(string Name, string Text, int TargetOriginalLine);

public class MarkerParseResult
{
    public required string ProcessedCode { get; init; }
    public required string OriginalCode { get; init; }
    public required List<HoverQuery> HoverQueries { get; init; }
    public required List<CompletionQuery> CompletionQueries { get; init; }
    public required List<ErrorExpectation> ErrorExpectations { get; init; }
    public required bool SuppressAllErrors { get; init; }
    public required List<string> SuppressedErrorCodes { get; init; }
    public required List<HiddenRange> HiddenRanges { get; init; }
    public required List<HighlightDirective> Highlights { get; init; }
    public required List<TagDirective> Tags { get; init; }
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
    private static readonly Regex CutBeforeMarkerRegex = CutBeforeMarkerPattern();
    private static readonly Regex CutAfterMarkerRegex = CutAfterMarkerPattern();
    private static readonly Regex CutStartDirectiveRegex = CutStartDirectivePattern();
    private static readonly Regex CutEndDirectiveRegex = CutEndDirectivePattern();
    private static readonly Regex HighlightDirectiveRegex = HighlightDirectivePattern();
    private static readonly Regex FocusDirectiveRegex = FocusDirectivePattern();
    private static readonly Regex DiffDirectiveRegex = DiffDirectivePattern();
    private static readonly Regex LangVersionDirectiveRegex = LangVersionDirectivePattern();
    private static readonly Regex NullableDirectiveRegex = NullableDirectivePattern();
    private static readonly Regex CustomTagDirectiveRegex = CustomTagDirectivePattern();

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

    [GeneratedRegex(@"^\s*//\s*(---cut(?:-before)?---)\s*$")]
    private static partial Regex CutBeforeMarkerPattern();

    [GeneratedRegex(@"^\s*//\s*(---cut-after---)\s*$")]
    private static partial Regex CutAfterMarkerPattern();

    [GeneratedRegex(@"^\s*//\s*(---cut-start---)\s*$")]
    private static partial Regex CutStartDirectivePattern();

    [GeneratedRegex(@"^\s*//\s*(---cut-end---)\s*$")]
    private static partial Regex CutEndDirectivePattern();

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

    [GeneratedRegex(@"^\s*//\s*@(log|warn|error|annotate):\s*(\S.*?)\s*$")]
    private static partial Regex CustomTagDirectivePattern();

    public static MarkerParseResult Parse(string source)
    {
        var lines = source.Split('\n');
        var hoverQueries = new List<HoverQuery>();
        var completionQueries = new List<CompletionQuery>();
        var errorExpectations = new List<ErrorExpectation>();
        var hiddenRanges = new List<HiddenRange>();
        var highlights = new List<HighlightDirective>();
        var tags = new List<TagDirective>();
        var suppressAllErrors = false;
        var suppressedErrorCodes = new List<string>();
        string? langVersion = null;
        string? nullable = null;

        // Track range-based directives to resolve after line mapping
        var rangeDirectives = new List<(string Kind, int StartLine, int EndLine)>(); // 1-based output lines

        // First pass: identify marker lines and collect metadata
        var isMarkerLine = new bool[lines.Length];
        var cutBeforeLine = -1;
        var cutAfterLine = -1;
        var cutStartLine = -1;
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

            // @noErrors (twoslash-compatible alias for @suppressErrors)
            if (NoErrorsDirectiveRegex.IsMatch(line))
            {
                suppressAllErrors = true;
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

            // ---cut--- or ---cut-before--- (first occurrence wins)
            if (CutBeforeMarkerRegex.IsMatch(line))
            {
                if (cutBeforeLine < 0)
                    cutBeforeLine = i;
                isMarkerLine[i] = true;
                continue;
            }

            // ---cut-after---
            if (CutAfterMarkerRegex.IsMatch(line))
            {
                if (cutAfterLine < 0)
                    cutAfterLine = i;
                isMarkerLine[i] = true;
                continue;
            }

            // ---cut-start---
            if (CutStartDirectiveRegex.IsMatch(line))
            {
                cutStartLine = i;
                isMarkerLine[i] = true;
                continue;
            }

            // ---cut-end---
            if (CutEndDirectiveRegex.IsMatch(line))
            {
                if (cutStartLine >= 0)
                {
                    for (var h = cutStartLine; h <= i; h++)
                        isHiddenLine[h] = true;
                    hiddenRanges.Add(new HiddenRange(cutStartLine, i));
                    cutStartLine = -1;
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

            // @log: message, @warn: message, @error: message, @annotate: message
            var tagMatch = CustomTagDirectiveRegex.Match(line);
            if (tagMatch.Success)
            {
                var tagName = tagMatch.Groups[1].Value;
                var tagText = tagMatch.Groups[2].Value.Trim();
                // Preserve the directive's own line index so remapping can resolve
                // it to the nearest preceding processed line, even if the immediately
                // preceding original code line is later hidden.
                tags.Add(new TagDirective(tagName, tagText, i));
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

        // If ---cut-start--- was never closed, hide to end of file
        if (cutStartLine >= 0)
        {
            for (var h = cutStartLine; h < lines.Length; h++)
                isHiddenLine[h] = true;
            hiddenRanges.Add(new HiddenRange(cutStartLine, lines.Length - 1));
        }

        // If there's a cut-before marker, everything before it (inclusive) is hidden
        if (cutBeforeLine >= 0)
        {
            for (var h = 0; h <= cutBeforeLine; h++)
                isHiddenLine[h] = true;
            hiddenRanges.Insert(0, new HiddenRange(0, cutBeforeLine));
        }

        // If there's a cut-after marker, everything after it (inclusive) is hidden
        if (cutAfterLine >= 0)
        {
            for (var h = cutAfterLine; h < lines.Length; h++)
                isHiddenLine[h] = true;
            hiddenRanges.Add(new HiddenRange(cutAfterLine, lines.Length - 1));
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

        // Remap tag directives — resolve each tag's original line index to the
        // nearest preceding processed line. This handles tags after @cut markers
        // where the immediately preceding code line may be hidden.
        var remappedTags = new List<TagDirective>();
        foreach (var tag in tags)
        {
            var processedLine = FindPrecedingProcessedLine(lineMap, tag.TargetOriginalLine);
            remappedTags.Add(new TagDirective(tag.Name, tag.Text, processedLine >= 0 ? processedLine : 0));
        }

        return new MarkerParseResult
        {
            ProcessedCode = string.Join('\n', processedLines),
            OriginalCode = source,
            HoverQueries = remappedQueries,
            CompletionQueries = remappedCompletions,
            ErrorExpectations = remappedErrors,
            SuppressAllErrors = suppressAllErrors,
            SuppressedErrorCodes = suppressedErrorCodes,
            HiddenRanges = hiddenRanges,
            Highlights = remappedHighlights,
            Tags = remappedTags,
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
                CutBeforeMarkerRegex.IsMatch(line) ||
                CutAfterMarkerRegex.IsMatch(line) ||
                CutStartDirectiveRegex.IsMatch(line) ||
                CutEndDirectiveRegex.IsMatch(line) ||
                HighlightDirectiveRegex.IsMatch(line) ||
                FocusDirectiveRegex.IsMatch(line) ||
                DiffDirectiveRegex.IsMatch(line) ||
                LangVersionDirectiveRegex.IsMatch(line) ||
                NullableDirectiveRegex.IsMatch(line) ||
                CustomTagDirectiveRegex.IsMatch(line))
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

    /// <summary>
    /// Find the nearest preceding processed line for an original line index.
    /// Walks backward through lineMap entries to find one at or before the given original line.
    /// </summary>
    private static int FindPrecedingProcessedLine(List<int> lineMap, int originalLine)
    {
        var best = -1;
        for (var i = 0; i < lineMap.Count; i++)
        {
            if (lineMap[i] <= originalLine)
                best = i;
        }
        return best;
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
                !CutBeforeMarkerRegex.IsMatch(line) &&
                !CutAfterMarkerRegex.IsMatch(line) &&
                !CutStartDirectiveRegex.IsMatch(line) &&
                !CutEndDirectiveRegex.IsMatch(line) &&
                !HighlightDirectiveRegex.IsMatch(line) &&
                !FocusDirectiveRegex.IsMatch(line) &&
                !DiffDirectiveRegex.IsMatch(line) &&
                !LangVersionDirectiveRegex.IsMatch(line) &&
                !NullableDirectiveRegex.IsMatch(line) &&
                !CustomTagDirectiveRegex.IsMatch(line))
            {
                return i;
            }
        }
        return -1;
    }
}
