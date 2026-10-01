using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>
/// The language version and nullable context a snippet asks for, from its <c>@langVersion</c> /
/// <c>@nullable</c> markers or the config. Null means "use the compilation context's default".
/// Invalid values are reported as GS0001/GS0002 in <see cref="Errors"/>.
/// </summary>
internal sealed record RequestedSettings(
    LanguageVersion? LangVersion,
    NullableContextOptions? Nullable,
    List<GloSharpError> Errors)
{
    /// <summary>Resolves the settings with precedence marker &gt; config.</summary>
    public static RequestedSettings Resolve(Snippet snippet, string? configLangVersion, string? configNullable)
    {
        var errors = new List<GloSharpError>();
        var markers = snippet.Markers;

        var langVersion = Pick(
            configLangVersion, markers.LangVersion, snippet.ToSourceLine(markers.LangVersionLine),
            CompilationOptionsMapper.MapLangVersion, GloSharpDiagnosticCodes.InvalidLangVersion,
            "language version", CompilationOptionsMapper.ValidLangVersions, errors);

        var nullable = Pick(
            configNullable, markers.Nullable, snippet.ToSourceLine(markers.NullableLine),
            CompilationOptionsMapper.MapNullable, GloSharpDiagnosticCodes.InvalidNullable,
            "nullable context", CompilationOptionsMapper.ValidNullableValues, errors);

        return new RequestedSettings(langVersion, nullable, errors);
    }

    /// <summary>
    /// Maps the config value, then the marker value (which wins when valid). Each invalid value
    /// adds an error; a config value has no source line, so it is reported at line 0.
    /// </summary>
    private static T? Pick<T>(
        string? configValue,
        string? markerValue,
        int markerSourceLine,
        Func<string, T?> map,
        string code,
        string description,
        string validValues,
        List<GloSharpError> errors) where T : struct
    {
        T? Map(string value, int sourceLine)
        {
            var mapped = map(value);
            if (mapped == null)
                errors.Add(ConfigurationError(code, $"Invalid {description} '{value}'. Valid values: {validValues}", sourceLine));
            return mapped;
        }

        T? result = null;
        if (configValue != null)
            result = Map(configValue, sourceLine: 0);
        if (markerValue != null)
            result = Map(markerValue, markerSourceLine) ?? result;
        return result;
    }

    private static GloSharpError ConfigurationError(string code, string message, int sourceLine) => new()
    {
        Line = 0,
        Character = 0,
        Length = 0,
        Code = code,
        Message = message,
        Severity = "error",
        Expected = false,
        SourceLine = sourceLine,
        SourceCharacter = 0,
    };
}
