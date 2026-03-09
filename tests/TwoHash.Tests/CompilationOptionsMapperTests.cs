using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using TwoHash.Core;

namespace TwoHash.Tests;

public class CompilationOptionsMapperTests
{
    [Test]
    [Arguments("7", LanguageVersion.CSharp7)]
    [Arguments("7.1", LanguageVersion.CSharp7_1)]
    [Arguments("7.2", LanguageVersion.CSharp7_2)]
    [Arguments("7.3", LanguageVersion.CSharp7_3)]
    [Arguments("8", LanguageVersion.CSharp8)]
    [Arguments("9", LanguageVersion.CSharp9)]
    [Arguments("10", LanguageVersion.CSharp10)]
    [Arguments("11", LanguageVersion.CSharp11)]
    [Arguments("12", LanguageVersion.CSharp12)]
    [Arguments("13", LanguageVersion.CSharp13)]
    public async Task MapLangVersion_NumericVersions_ReturnsCorrectEnum(string input, LanguageVersion expected)
    {
        var result = CompilationOptionsMapper.MapLangVersion(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("latest", LanguageVersion.Latest)]
    [Arguments("preview", LanguageVersion.Preview)]
    [Arguments("default", LanguageVersion.Default)]
    public async Task MapLangVersion_NamedVersions_ReturnsCorrectEnum(string input, LanguageVersion expected)
    {
        var result = CompilationOptionsMapper.MapLangVersion(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Latest")]
    [Arguments("PREVIEW")]
    public async Task MapLangVersion_CaseInsensitive(string input)
    {
        var result = CompilationOptionsMapper.MapLangVersion(input);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    [Arguments("99")]
    [Arguments("abc")]
    [Arguments("")]
    public async Task MapLangVersion_InvalidValues_ReturnsNull(string input)
    {
        var result = CompilationOptionsMapper.MapLangVersion(input);
        await Assert.That(result).IsNull();
    }

    [Test]
    [Arguments("enable", NullableContextOptions.Enable)]
    [Arguments("disable", NullableContextOptions.Disable)]
    [Arguments("warnings", NullableContextOptions.Warnings)]
    [Arguments("annotations", NullableContextOptions.Annotations)]
    public async Task MapNullable_ValidValues_ReturnsCorrectEnum(string input, NullableContextOptions expected)
    {
        var result = CompilationOptionsMapper.MapNullable(input);
        await Assert.That(result).IsEqualTo(expected);
    }

    [Test]
    [Arguments("Enable")]
    [Arguments("DISABLE")]
    public async Task MapNullable_CaseInsensitive(string input)
    {
        var result = CompilationOptionsMapper.MapNullable(input);
        await Assert.That(result).IsNotNull();
    }

    [Test]
    [Arguments("sometimes")]
    [Arguments("true")]
    [Arguments("")]
    public async Task MapNullable_InvalidValues_ReturnsNull(string input)
    {
        var result = CompilationOptionsMapper.MapNullable(input);
        await Assert.That(result).IsNull();
    }
}
