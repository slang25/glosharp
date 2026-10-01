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

    /// <summary>Diagnostic code for a non-fatal processing warning (<c>meta.warnings</c>).</summary>
    public const string ProcessingWarningCode = "GS1003";

    /// <summary>Formats one snippet diagnostic for the file it came from.</summary>
    public static string Format(string filePath, GloSharpError error)
    {
        // Report positions in the original file: Line/Character index the processed `code`, which
        // drifts upwards whenever #: directives, markers, cut or region lines precede the error.
        // Hidden-code errors have no processed position (Line is -1) and always carry SourceLine.
        var line = (error.SourceLine ?? Math.Max(error.Line, 0)) + 1;
        var column = (error.SourceCharacter ?? Math.Max(error.Character, 0)) + 1;
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
