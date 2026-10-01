using System.Text.RegularExpressions;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

// Line-index naming used in this file:
// - "input line": 0-based line of the text handed to MarkerParser.Parse.
// - "processed line": 0-based line of MarkerParseResult.ProcessedCode (what is rendered).
// - "compilation line": 0-based line of MarkerParseResult.CompilationCode (what is compiled).
// For historical reasons the records below call their (remapped) processed line "OriginalLine".

public record HoverQuery(int OriginalLine, int Column)
{
    /// <summary>Input line of the <c>^?</c> marker itself, or -1.</summary>
    public int MarkerInputLine { get; init; } = -1;
}

public record CompletionQuery(int OriginalLine, int Column)
{
    /// <summary>Input line of the <c>^|</c> marker itself, or -1.</summary>
    public int MarkerInputLine { get; init; } = -1;
}

/// <summary>
/// An <c>// @errors:</c> expectation. <see cref="OriginalLine"/> is the processed line of the
/// target (or -1 when the target is hidden); <see cref="InputLine"/> is the target's input line.
/// </summary>
public record ErrorExpectation(int OriginalLine, List<string> Codes)
{
    public int InputLine { get; init; } = -1;
}

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
    public required int[] LineMap { get; init; } // processedLine -> input line
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }

    /// <summary>Input line of the <c>@langVersion</c> directive that set <see cref="LangVersion"/>, or -1.</summary>
    public int LangVersionLine { get; init; } = -1;

    /// <summary>Input line of the <c>@nullable</c> directive that set <see cref="Nullable"/>, or -1.</summary>
    public int NullableLine { get; init; } = -1;

    /// <summary>All non-marker lines, including hidden ones — the text that gets compiled.</summary>
    public string CompilationCode { get; init; } = "";

    /// <summary>compilation line -> input line.</summary>
    public int[] CompilationLineMap { get; init; } = [];

    /// <summary>input line -> processed line, or -1 for marker and hidden lines.</summary>
    public int[] InputLineToProcessed { get; init; } = [];

    /// <summary>input line -> compilation line, or -1 for marker lines.</summary>
    public int[] InputLineToCompilation { get; init; } = [];
}

/// <summary>An inclusive range of hidden input lines.</summary>
public record HiddenRange(int StartLine, int EndLine);

public static partial class MarkerParser
{
    private enum MarkerKind
    {
        None,
        Hover,
        Completion,
        Errors,
        NoErrors,
        SuppressErrors,
        CutBefore,
        CutAfter,
        CutStart,
        CutEnd,
        Highlight,
        Focus,
        Diff,
        LangVersion,
        Nullable,
        Tag,
    }

    private static readonly (MarkerKind Kind, Regex Regex)[] MarkerPatterns =
    [
        (MarkerKind.Hover, HoverMarkerPattern()),
        (MarkerKind.Completion, CompletionMarkerPattern()),
        (MarkerKind.Errors, ErrorsDirectivePattern()),
        (MarkerKind.NoErrors, NoErrorsDirectivePattern()),
        (MarkerKind.SuppressErrors, SuppressErrorsDirectivePattern()),
        (MarkerKind.CutBefore, CutBeforeMarkerPattern()),
        (MarkerKind.CutAfter, CutAfterMarkerPattern()),
        (MarkerKind.CutStart, CutStartDirectivePattern()),
        (MarkerKind.CutEnd, CutEndDirectivePattern()),
        (MarkerKind.LangVersion, LangVersionDirectivePattern()),
        (MarkerKind.Nullable, NullableDirectivePattern()),
        (MarkerKind.Tag, CustomTagDirectivePattern()),
        (MarkerKind.Highlight, HighlightDirectivePattern()),
        (MarkerKind.Focus, FocusDirectivePattern()),
        (MarkerKind.Diff, DiffDirectivePattern()),
    ];

    private static readonly char[] CodeSeparators = [',', ' ', '\t', ';'];

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

    public static MarkerParseResult Parse(string source) => Parse(source, null);

    /// <summary>
    /// Parses markers in <paramref name="source"/>.
    /// </summary>
    /// <param name="source">Snippet text (after <c>#:</c> directive stripping).</param>
    /// <param name="hiddenLines">
    /// Optional per-line mask of additional lines to hide from the output while still compiling
    /// them (used for <c>--region</c>). Indexed like <c>source.Split('\n')</c>.
    /// </param>
    public static MarkerParseResult Parse(string source, bool[]? hiddenLines)
    {
        var lines = source.Split('\n');
        var (kinds, matches) = ClassifyLines(source, lines);

        var isMarkerLine = new bool[lines.Length];
        for (var i = 0; i < lines.Length; i++)
            isMarkerLine[i] = kinds[i] != MarkerKind.None;

        var hoverQueries = new List<HoverQuery>();
        var completionQueries = new List<CompletionQuery>();
        var errorExpectations = new List<(int TargetLine, List<string> Codes)>();
        var highlights = new List<HighlightDirective>(); // TargetOriginalLine = input line until remapped
        var tags = new List<TagDirective>();             // TargetOriginalLine = directive's input line until remapped
        var suppressAllErrors = false;
        var suppressedErrorCodes = new List<string>();
        string? langVersion = null;
        string? nullable = null;
        var langVersionLine = -1;
        var nullableLine = -1;

        // Range-based directives use 1-based output line numbers; resolved after line mapping
        var rangeDirectives = new List<(string Kind, int StartLine, int EndLine)>();

        var cutBeforeLine = -1;
        var cutAfterLine = -1;
        var cutStartLine = -1;
        var cutDepth = 0;
        var isHiddenLine = new bool[lines.Length];

        if (hiddenLines != null)
        {
            for (var i = 0; i < lines.Length && i < hiddenLines.Length; i++)
                isHiddenLine[i] = hiddenLines[i];
        }

        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var match = matches[i];

            switch (kinds[i])
            {
                case MarkerKind.Hover:
                case MarkerKind.Completion:
                {
                    // The caret targets the nearest preceding non-marker line
                    var caretCol = line.IndexOf('^');
                    var target = FindPrecedingCodeLine(isMarkerLine, i);
                    if (caretCol >= 0 && target >= 0)
                    {
                        if (kinds[i] == MarkerKind.Hover)
                            hoverQueries.Add(new HoverQuery(target, caretCol) { MarkerInputLine = i });
                        else
                            completionQueries.Add(new CompletionQuery(target, caretCol) { MarkerInputLine = i });
                    }
                    break;
                }

                case MarkerKind.Errors:
                {
                    // Error expectations apply to the next code line
                    var codes = SplitCodes(match!.Groups[1].Value);
                    var target = FindNextCodeLine(isMarkerLine, i);
                    if (target >= 0 && codes.Count > 0)
                        errorExpectations.Add((target, codes));
                    break;
                }

                case MarkerKind.NoErrors:
                    suppressAllErrors = true;
                    break;

                case MarkerKind.SuppressErrors:
                {
                    var codes = SplitCodes(match!.Groups[1].Value);
                    if (codes.Count == 0)
                        suppressAllErrors = true;
                    else
                        suppressedErrorCodes.AddRange(codes);
                    break;
                }

                case MarkerKind.CutBefore:
                    // First occurrence wins
                    if (cutBeforeLine < 0)
                        cutBeforeLine = i;
                    break;

                case MarkerKind.CutAfter:
                    if (cutAfterLine < 0)
                        cutAfterLine = i;
                    break;

                case MarkerKind.CutStart:
                    // Cut blocks nest: only the outermost start/end pair defines the hidden range
                    if (cutDepth == 0)
                        cutStartLine = i;
                    cutDepth++;
                    break;

                case MarkerKind.CutEnd:
                    if (cutDepth > 0)
                    {
                        cutDepth--;
                        if (cutDepth == 0)
                        {
                            for (var h = cutStartLine; h <= i; h++)
                                isHiddenLine[h] = true;
                            cutStartLine = -1;
                        }
                    }
                    break;

                case MarkerKind.LangVersion:
                    langVersion = match!.Groups[1].Value.Trim().ToLowerInvariant();
                    langVersionLine = i;
                    break;

                case MarkerKind.Nullable:
                    nullable = match!.Groups[1].Value.Trim().ToLowerInvariant();
                    nullableLine = i;
                    break;

                case MarkerKind.Tag:
                    // Keep the directive's own line so remapping can resolve it to the nearest
                    // preceding processed line, even if the preceding code line is hidden.
                    tags.Add(new TagDirective(match!.Groups[1].Value, match.Groups[2].Value.Trim(), i));
                    break;

                case MarkerKind.Highlight:
                case MarkerKind.Focus:
                {
                    var kind = kinds[i] == MarkerKind.Highlight ? "highlight" : "focus";
                    var arg = match!.Groups[1].Value.Trim();
                    if (string.IsNullOrEmpty(arg))
                    {
                        // Bare directive — targets next code line
                        var target = FindNextCodeLine(isMarkerLine, i);
                        if (target >= 0)
                            highlights.Add(new HighlightDirective(kind, target));
                    }
                    else
                    {
                        ParseLineRange(arg, kind, rangeDirectives);
                    }
                    break;
                }

                case MarkerKind.Diff:
                {
                    var kind = match!.Groups[1].Value == "+" ? "add" : "remove";
                    var target = FindNextCodeLine(isMarkerLine, i);
                    if (target >= 0)
                        highlights.Add(new HighlightDirective(kind, target));
                    break;
                }
            }
        }

        // An unclosed ---cut-start--- hides to end of file
        if (cutDepth > 0)
        {
            for (var h = cutStartLine; h < lines.Length; h++)
                isHiddenLine[h] = true;
        }

        // Everything up to and including a cut-before marker is hidden
        if (cutBeforeLine >= 0)
        {
            for (var h = 0; h <= cutBeforeLine; h++)
                isHiddenLine[h] = true;
        }

        // Everything from a cut-after marker onwards is hidden
        if (cutAfterLine >= 0)
        {
            for (var h = cutAfterLine; h < lines.Length; h++)
                isHiddenLine[h] = true;
        }

        // Build processed + compilation code and the line maps (all O(1) lookups afterwards)
        var processedLines = new List<string>();
        var lineMap = new List<int>();
        var compilationLines = new List<string>();
        var compilationLineMap = new List<int>();
        var inputToProcessed = new int[lines.Length];
        var inputToCompilation = new int[lines.Length];
        // For each input line, the processed line at or before it (-1 if none)
        var precedingProcessed = new int[lines.Length];

        for (var i = 0; i < lines.Length; i++)
        {
            if (isMarkerLine[i])
            {
                inputToCompilation[i] = -1;
            }
            else
            {
                inputToCompilation[i] = compilationLines.Count;
                compilationLines.Add(lines[i]);
                compilationLineMap.Add(i);
            }

            if (isMarkerLine[i] || isHiddenLine[i])
            {
                inputToProcessed[i] = -1;
            }
            else
            {
                inputToProcessed[i] = processedLines.Count;
                processedLines.Add(lines[i]);
                lineMap.Add(i);
            }

            precedingProcessed[i] = processedLines.Count - 1;
        }

        // Resolve range-based directives (1-based output line numbers → input lines)
        foreach (var (kind, startLine, endLine) in rangeDirectives)
        {
            for (var lineNum = startLine; lineNum <= endLine; lineNum++)
            {
                var processedLine = lineNum - 1;
                if (processedLine >= 0 && processedLine < processedLines.Count)
                    highlights.Add(new HighlightDirective(kind, lineMap[processedLine]));
            }
        }

        var remappedQueries = hoverQueries
            .Where(q => inputToProcessed[q.OriginalLine] >= 0)
            .Select(q => q with { OriginalLine = inputToProcessed[q.OriginalLine] })
            .ToList();

        var remappedCompletions = completionQueries
            .Where(q => inputToProcessed[q.OriginalLine] >= 0)
            .Select(q => q with { OriginalLine = inputToProcessed[q.OriginalLine] })
            .ToList();

        // Expectations are kept even when their target is hidden (OriginalLine = -1) so that
        // errors in hidden code can still be marked as expected.
        var remappedErrors = errorExpectations
            .Select(e => new ErrorExpectation(inputToProcessed[e.TargetLine], e.Codes) { InputLine = e.TargetLine })
            .ToList();

        var remappedHighlights = highlights
            .Where(h => inputToProcessed[h.TargetOriginalLine] >= 0)
            .Select(h => new HighlightDirective(h.Kind, inputToProcessed[h.TargetOriginalLine]))
            .ToList();

        var remappedTags = tags
            .Select(t => new TagDirective(t.Name, t.Text, Math.Max(0, precedingProcessed[t.TargetOriginalLine])))
            .ToList();

        return new MarkerParseResult
        {
            ProcessedCode = string.Join('\n', processedLines),
            OriginalCode = source,
            HoverQueries = remappedQueries,
            CompletionQueries = remappedCompletions,
            ErrorExpectations = remappedErrors,
            SuppressAllErrors = suppressAllErrors,
            SuppressedErrorCodes = suppressedErrorCodes,
            HiddenRanges = BuildHiddenRanges(isHiddenLine),
            Highlights = remappedHighlights,
            Tags = remappedTags,
            LineMap = lineMap.ToArray(),
            LangVersion = langVersion,
            Nullable = nullable,
            LangVersionLine = langVersionLine,
            NullableLine = nullableLine,
            CompilationCode = string.Join('\n', compilationLines),
            CompilationLineMap = compilationLineMap.ToArray(),
            InputLineToProcessed = inputToProcessed,
            InputLineToCompilation = inputToCompilation,
        };
    }

    /// <summary>
    /// For compilation, we need all lines including hidden ones but without marker lines.
    /// </summary>
    public static string GetCompilationCode(string source) => Parse(source).CompilationCode;

    /// <summary>
    /// Splits an <c>@errors</c>/<c>@suppressErrors</c> argument. Codes may be separated by commas
    /// and/or whitespace (twoslash style), e.g. <c>CS0029, CS1503</c> or <c>CS0029 CS1503</c>.
    /// </summary>
    internal static List<string> SplitCodes(string value) =>
        value.Split(CodeSeparators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// Classifies every line as a marker kind (or None). A line that matches a marker pattern is
    /// only treated as a marker if its <c>//</c> really starts a comment — lines inside string
    /// literals (raw, verbatim, interpolated) are code and are left alone.
    /// </summary>
    private static (MarkerKind[] Kinds, Match?[] Matches) ClassifyLines(string source, string[] lines)
    {
        var kinds = new MarkerKind[lines.Length];
        var matches = new Match?[lines.Length];
        var anyMarker = false;

        for (var i = 0; i < lines.Length; i++)
        {
            foreach (var (kind, regex) in MarkerPatterns)
            {
                var m = regex.Match(lines[i]);
                if (m.Success)
                {
                    kinds[i] = kind;
                    matches[i] = m;
                    anyMarker = true;
                    break;
                }
            }
        }

        if (!anyMarker)
            return (kinds, matches);

        // Only pay for a parse if a line could be inside a multi-line string literal.
        if (!source.Contains("\"\"\"") && !source.Contains("@\"") && !source.Contains("$\""))
            return (kinds, matches);

        var root = CSharpSyntaxTree.ParseText(source).GetRoot();
        var lineStart = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            if (kinds[i] != MarkerKind.None)
            {
                var commentStart = lineStart + lines[i].IndexOf("//", StringComparison.Ordinal);
                var token = root.FindToken(commentStart);
                if (token.Span.Contains(commentStart))
                {
                    kinds[i] = MarkerKind.None;
                    matches[i] = null;
                }
            }
            lineStart += lines[i].Length + 1;
        }

        return (kinds, matches);
    }

    private static List<HiddenRange> BuildHiddenRanges(bool[] isHiddenLine)
    {
        var ranges = new List<HiddenRange>();
        var start = -1;
        for (var i = 0; i <= isHiddenLine.Length; i++)
        {
            var hidden = i < isHiddenLine.Length && isHiddenLine[i];
            if (hidden && start < 0)
            {
                start = i;
            }
            else if (!hidden && start >= 0)
            {
                ranges.Add(new HiddenRange(start, i - 1));
                start = -1;
            }
        }
        return ranges;
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

    private static int FindPrecedingCodeLine(bool[] isMarkerLine, int fromLine)
    {
        for (var i = fromLine - 1; i >= 0; i--)
        {
            if (!isMarkerLine[i])
                return i;
        }
        return -1;
    }

    private static int FindNextCodeLine(bool[] isMarkerLine, int fromLine)
    {
        for (var i = fromLine + 1; i < isMarkerLine.Length; i++)
        {
            if (!isMarkerLine[i])
                return i;
        }
        return -1;
    }
}
