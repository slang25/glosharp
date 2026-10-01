namespace GloSharp.Core;

/// <summary>
/// Diagnostic codes Glo# itself reports in <c>errors</c>/<c>hiddenErrors</c> (as opposed to
/// compiler diagnostics). Documented in design/data-format.md.
/// </summary>
public static class GloSharpDiagnosticCodes
{
    /// <summary>Invalid <c>@langVersion</c> marker or <c>langVersion</c> config value.</summary>
    public const string InvalidLangVersion = "GS0001";

    /// <summary>Invalid <c>@nullable</c> marker or <c>nullable</c> config value.</summary>
    public const string InvalidNullable = "GS0002";

    /// <summary>An <c>// @errors:</c> expectation whose diagnostic was not reported on its target line.</summary>
    public const string UnmatchedExpectedError = "GS0003";
}
