using GloSharp.Core;

namespace GloSharp.Cli;

/// <summary>
/// Formats diagnostics in the MSBuild canonical error format,
/// <c>path(line,col): error CODE: message [path]</c>, which IDEs, terminals and GitHub's
/// problem matchers (actions/setup-dotnet registers one for csc output) recognise and turn
/// into clickable locations / pull request annotations. Paths are absolute so the matcher's
/// optional "from path" (the bracketed part) can't re-root them incorrectly.
/// </summary>
internal static class DiagnosticFormatter
{
    /// <summary>Diagnostic code for a snippet file that could not be processed at all.</summary>
    public const string ProcessingFailedCode = "GS1001";

    /// <summary>Diagnostic code for a failed snippet whose errors have no visible location.</summary>
    public const string VerificationFailedCode = "GS1002";

    /// <summary>Formats one snippet diagnostic for the file it came from.</summary>
    public static string Format(string filePath, GloSharpError error)
    {
        // TODO(sourceLine): switch to error.SourceLine / error.SourceCharacter once GloSharpError
        // carries them (0-based positions in the ORIGINAL file text, before #: directives,
        // markers, cut and region lines are stripped). Line/Character index the processed
        // `code`, so they drift upwards whenever such lines precede the error.
        var line = error.Line + 1;
        var column = error.Character + 1;
        return FormatCanonical(filePath, line, column, error.Severity, error.Code, error.Message);
    }

    /// <summary>A file-level failure (exception) reported at line 1 so it still annotates the file.</summary>
    public static string FormatFileFailure(string filePath, string message) =>
        FormatCanonical(filePath, 1, 1, "error", ProcessingFailedCode, message);

    public static string FormatCanonical(string filePath, int line, int column, string severity, string code, string message)
    {
        var path = Path.GetFullPath(filePath);
        var category = severity is "warning" ? "warning" : "error";
        var text = string.Join(' ', message.Split(['\r', '\n'], StringSplitOptions.RemoveEmptyEntries).Select(s => s.Trim()));
        return $"{path}({line},{column}): {category} {code}: {text} [{path}]";
    }
}
