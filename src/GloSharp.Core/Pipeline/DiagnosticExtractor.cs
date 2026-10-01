using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <param name="Errors">Diagnostics placed in the processed code (<c>errors</c>).</param>
/// <param name="HiddenErrors">Errors in hidden code (<c>hiddenErrors</c>, <c>line</c> -1).</param>
/// <param name="CompileSucceeded">False when any error is neither expected nor suppressed, or an expectation went unmatched.</param>
internal sealed record DiagnosticResult(
    List<GloSharpError> Errors,
    List<GloSharpError> HiddenErrors,
    bool CompileSucceeded);

/// <summary>
/// Classifies a snippet's compiler diagnostics against its <c>@errors</c> expectations and
/// <c>@noErrors</c>/<c>@suppressErrors</c> markers: visible vs hidden, expected vs unexpected,
/// suppressed, and GS0003 for expectations nothing satisfied.
/// </summary>
internal static class DiagnosticExtractor
{
    /// <param name="model">The semantic model of the snippet's own syntax tree.</param>
    public static DiagnosticResult Extract(Snippet snippet, SemanticModel model) =>
        Classify(snippet, model.SyntaxTree, model.GetDiagnostics());

    /// <summary>
    /// Classifies <paramref name="diagnostics"/>. Only info-or-higher diagnostics located in
    /// <paramref name="tree"/> (the snippet's own tree) are considered.
    /// </summary>
    internal static DiagnosticResult Classify(Snippet snippet, SyntaxTree tree, IEnumerable<Diagnostic> diagnostics)
    {
        var markers = snippet.Markers;
        var lines = snippet.CompilationLines;
        var errors = new List<GloSharpError>();
        var hiddenErrors = new List<GloSharpError>();
        var hasUnexpectedErrors = false;

        // @errors expectations, keyed by the input line they target (which may be hidden)
        var expectations = markers.ErrorExpectations
            .GroupBy(e => e.InputLine)
            .ToDictionary(g => g.Key, g => g.SelectMany(e => e.Codes).ToHashSet(StringComparer.Ordinal));
        var matched = new HashSet<(int InputLine, string Code)>();

        bool IsSuppressed(string code) =>
            markers.SuppressAllErrors || markers.SuppressedErrorCodes.Contains(code);

        var relevant = diagnostics
            .Where(d => d.Severity >= DiagnosticSeverity.Info)
            .Where(d => d.Location.IsInSource && d.Location.SourceTree == tree)
            .OrderBy(d => d.Location.SourceSpan.Start);

        foreach (var diagnostic in relevant)
        {
            var span = diagnostic.Location.SourceSpan;
            var (compLine, character) = lines.GetPosition(span.Start);
            var inputLine = snippet.CompilationToInputLine(compLine);
            var processedLine = markers.InputLineToProcessed[inputLine];
            var code = diagnostic.Id;

            // Expectations are matched before suppression so a suppressed diagnostic
            // still satisfies its @errors line.
            var expected = expectations.TryGetValue(inputLine, out var expectedCodes) && expectedCodes.Contains(code);
            if (expected)
                matched.Add((inputLine, code));

            // Block-level suppression (@noErrors / @suppressErrors) hides errors, warnings and info
            if (IsSuppressed(code))
                continue;

            var severity = diagnostic.Severity switch
            {
                DiagnosticSeverity.Error => "error",
                DiagnosticSeverity.Warning => "warning",
                DiagnosticSeverity.Info => "info",
                _ => "hidden",
            };

            var sourceLine = snippet.ToSourceLine(inputLine);

            if (processedLine < 0)
            {
                // Hidden (cut/region) code: errors still fail the snippet, but can't be placed
                // in the rendered code. Warnings/info in setup code are not reported.
                if (severity == "error")
                {
                    if (!expected) hasUnexpectedErrors = true;
                    hiddenErrors.Add(new GloSharpError
                    {
                        Line = -1,
                        Character = character,
                        Length = Math.Max(1, span.Length),
                        Code = code,
                        Message = diagnostic.GetMessage(),
                        Severity = severity,
                        Expected = expected,
                        SourceLine = sourceLine,
                        SourceCharacter = character,
                    });
                }
                continue;
            }

            if (severity == "error" && !expected)
                hasUnexpectedErrors = true;

            int? endLine = null;
            int? endCharacter = null;
            var (endCompLine, endChar) = lines.GetPosition(span.End);
            if (endCompLine != compLine)
            {
                var processedEndLine = snippet.CompilationToProcessedLine(endCompLine);
                if (processedEndLine >= 0)
                {
                    endLine = processedEndLine;
                    endCharacter = endChar;
                }
            }

            errors.Add(new GloSharpError
            {
                Line = processedLine,
                Character = character,
                Length = Math.Max(1, span.Length),
                EndLine = endLine,
                EndCharacter = endCharacter,
                Code = code,
                Message = diagnostic.GetMessage(),
                Severity = severity,
                Expected = expected,
                SourceLine = sourceLine,
                SourceCharacter = character,
            });
        }

        // @errors expectations that nothing satisfied: the snippet no longer demonstrates the
        // error it documents.
        foreach (var expectation in markers.ErrorExpectations)
        {
            foreach (var code in expectation.Codes)
            {
                if (matched.Contains((expectation.InputLine, code)) || IsSuppressed(code))
                    continue;
                matched.Add((expectation.InputLine, code)); // report each (line, code) once

                hasUnexpectedErrors = true;
                var compLine = markers.InputLineToCompilation[expectation.InputLine];
                var lineText = compLine >= 0 ? lines.GetLineText(compLine) : "";
                var indent = lineText.Length - lineText.TrimStart().Length;

                var error = new GloSharpError
                {
                    Line = expectation.OriginalLine,
                    Character = indent,
                    Length = Math.Max(1, lineText.Trim().Length),
                    Code = GloSharpDiagnosticCodes.UnmatchedExpectedError,
                    Message = $"Expected error {code} (from '// @errors') was not reported on this line.",
                    Severity = "error",
                    Expected = false,
                    SourceLine = snippet.ToSourceLine(expectation.InputLine),
                    SourceCharacter = indent,
                };

                if (expectation.OriginalLine >= 0)
                    errors.Add(error);
                else
                    hiddenErrors.Add(error);
            }
        }

        return new DiagnosticResult(errors, hiddenErrors, !hasUnexpectedErrors);
    }
}
