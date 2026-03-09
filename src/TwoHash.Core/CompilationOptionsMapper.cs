using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TwoHash.Core;

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
        return LangVersionMap.TryGetValue(value, out var result) ? result : null;
    }

    public static NullableContextOptions? MapNullable(string value)
    {
        return NullableMap.TryGetValue(value, out var result) ? result : null;
    }

    public static string ValidLangVersions => string.Join(", ", LangVersionMap.Keys);
    public static string ValidNullableValues => string.Join(", ", NullableMap.Keys);
}
