using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

public static class CompilationOptionsMapper
{
    private static readonly Dictionary<string, LanguageVersion> LangVersionMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["7"] = LanguageVersion.CSharp7,
        ["7.1"] = LanguageVersion.CSharp7_1,
        ["7.2"] = LanguageVersion.CSharp7_2,
        ["7.3"] = LanguageVersion.CSharp7_3,
        ["8"] = LanguageVersion.CSharp8,
        ["9"] = LanguageVersion.CSharp9,
        ["10"] = LanguageVersion.CSharp10,
        ["11"] = LanguageVersion.CSharp11,
        ["12"] = LanguageVersion.CSharp12,
        ["13"] = LanguageVersion.CSharp13,
        ["14"] = LanguageVersion.CSharp14,
        ["latest"] = LanguageVersion.Latest,
        ["preview"] = LanguageVersion.Preview,
        ["default"] = LanguageVersion.Default,
    };

    private static readonly Dictionary<string, NullableContextOptions> NullableMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["enable"] = NullableContextOptions.Enable,
        ["disable"] = NullableContextOptions.Disable,
        ["warnings"] = NullableContextOptions.Warnings,
        ["annotations"] = NullableContextOptions.Annotations,
    };

    public static LanguageVersion? MapLangVersion(string value)
    {
        if (LangVersionMap.TryGetValue(value, out var result))
            return result;

        // Accept anything the compiler accepts for -langversion (e.g. "12.0", "latestmajor")
        return LanguageVersionFacts.TryParse(value, out var parsed) ? parsed : null;
    }

    public static NullableContextOptions? MapNullable(string value)
    {
        return NullableMap.TryGetValue(value, out var result) ? result : null;
    }

    /// <summary>
    /// The user-facing spelling of a language version, as accepted by <see cref="MapLangVersion"/>
    /// (e.g. "12", "latest", "preview").
    /// </summary>
    public static string ToDisplayString(LanguageVersion version)
    {
        foreach (var (key, value) in LangVersionMap)
        {
            if (value == version)
                return key;
        }
        return version.ToDisplayString();
    }

    /// <summary>The user-facing spelling of a nullable context ("enable", "disable", ...).</summary>
    public static string ToDisplayString(NullableContextOptions nullable) => nullable switch
    {
        NullableContextOptions.Enable => "enable",
        NullableContextOptions.Disable => "disable",
        NullableContextOptions.Warnings => "warnings",
        NullableContextOptions.Annotations => "annotations",
        _ => nullable.ToString().ToLowerInvariant(),
    };

    public static string ValidLangVersions => string.Join(", ", LangVersionMap.Keys);
    public static string ValidNullableValues => string.Join(", ", NullableMap.Keys);
}
