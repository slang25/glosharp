using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

internal sealed class GloContextManifest
{
    public int Version { get; init; } = 1;
    public List<ManifestCompilation> Compilations { get; init; } = new();
}

internal sealed class ManifestCompilation
{
    public string ProjectName { get; init; } = "";
    public string TargetFramework { get; init; } = "";
    public string Language { get; init; } = "csharp";
    public ManifestCompilationOptions CompilationOptions { get; init; } = new();
    public ManifestParseOptions ParseOptions { get; init; } = new();
    public List<ManifestReference> References { get; init; } = new();
}

internal sealed class ManifestCompilationOptions
{
    public string OutputKind { get; init; } = "ConsoleApplication";
    public string NullableContext { get; init; } = "Disable";
    public string Platform { get; init; } = "AnyCpu";
    public bool AllowUnsafe { get; init; }
    public bool CheckOverflow { get; init; }
    public string? ModuleName { get; init; }
    public string? MainTypeName { get; init; }
    public bool Deterministic { get; init; }
    public string OptimizationLevel { get; init; } = "Debug";
    public int WarningLevel { get; init; } = 4;
    public string GeneralDiagnosticOption { get; init; } = "Default";
    public Dictionary<string, string> SpecificDiagnosticOptions { get; init; } = new();
    public List<string> Usings { get; init; } = new();
}

internal sealed class ManifestParseOptions
{
    public string LanguageVersion { get; init; } = "Latest";
    public string DocumentationMode { get; init; } = "Parse";
    public string Kind { get; init; } = "Regular";
    public List<string> PreprocessorSymbols { get; init; } = new();
    public Dictionary<string, string> Features { get; init; } = new();
}

internal sealed class ManifestReference
{
    public string Blob { get; init; } = "";
    public string Display { get; init; } = "";
    public List<string> Aliases { get; init; } = new();
    public bool EmbedInteropTypes { get; init; }
}

internal static class ManifestSerializer
{
    public static byte[] Serialize(GloContextManifest manifest)
    {
        var node = JsonSerializer.SerializeToNode(manifest, ManifestJsonContext.Default.GloContextManifest)
            ?? throw new InvalidOperationException("Manifest serialization returned null");
        SortKeys(node);
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream, new JsonWriterOptions { Indented = false }))
        {
            node.WriteTo(writer);
        }
        return stream.ToArray();
    }

    public static GloContextManifest Deserialize(ReadOnlySpan<byte> json)
    {
        return JsonSerializer.Deserialize(json, ManifestJsonContext.Default.GloContextManifest)
            ?? throw new InvalidDataException("Manifest JSON deserialized to null");
    }

    private static void SortKeys(JsonNode? node)
    {
        switch (node)
        {
            case JsonObject obj:
                var sorted = obj.OrderBy(kv => kv.Key, StringComparer.Ordinal).ToList();
                foreach (var kv in sorted)
                {
                    obj.Remove(kv.Key);
                }
                foreach (var kv in sorted)
                {
                    obj[kv.Key] = kv.Value;
                    SortKeys(kv.Value);
                }
                break;
            case JsonArray arr:
                foreach (var item in arr)
                {
                    SortKeys(item);
                }
                break;
        }
    }
}

[JsonSerializable(typeof(GloContextManifest))]
[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    WriteIndented = false)]
internal partial class ManifestJsonContext : JsonSerializerContext
{
}

internal static class ManifestOptionsMapper
{
    public static ManifestCompilationOptions Capture(CSharpCompilationOptions options)
    {
        return new ManifestCompilationOptions
        {
            OutputKind = options.OutputKind.ToString(),
            NullableContext = options.NullableContextOptions.ToString(),
            Platform = options.Platform.ToString(),
            AllowUnsafe = options.AllowUnsafe,
            CheckOverflow = options.CheckOverflow,
            ModuleName = options.ModuleName,
            MainTypeName = options.MainTypeName,
            Deterministic = options.Deterministic,
            OptimizationLevel = options.OptimizationLevel.ToString(),
            WarningLevel = options.WarningLevel,
            GeneralDiagnosticOption = options.GeneralDiagnosticOption.ToString(),
            SpecificDiagnosticOptions = options.SpecificDiagnosticOptions
                .ToDictionary(kv => kv.Key, kv => kv.Value.ToString(), StringComparer.Ordinal),
            Usings = options.Usings.ToList(),
        };
    }

    public static CSharpCompilationOptions Restore(ManifestCompilationOptions captured)
    {
        return new CSharpCompilationOptions(
                outputKind: Enum.Parse<OutputKind>(captured.OutputKind),
                allowUnsafe: captured.AllowUnsafe)
            .WithNullableContextOptions(Enum.Parse<NullableContextOptions>(captured.NullableContext))
            .WithPlatform(Enum.Parse<Platform>(captured.Platform))
            .WithOverflowChecks(captured.CheckOverflow)
            .WithModuleName(captured.ModuleName)
            .WithMainTypeName(captured.MainTypeName)
            .WithDeterministic(captured.Deterministic)
            .WithOptimizationLevel(Enum.Parse<OptimizationLevel>(captured.OptimizationLevel))
            .WithWarningLevel(captured.WarningLevel)
            .WithGeneralDiagnosticOption(Enum.Parse<ReportDiagnostic>(captured.GeneralDiagnosticOption))
            .WithSpecificDiagnosticOptions(captured.SpecificDiagnosticOptions
                .ToDictionary(kv => kv.Key, kv => Enum.Parse<ReportDiagnostic>(kv.Value), StringComparer.Ordinal))
            .WithUsings(captured.Usings);
    }

    public static ManifestParseOptions Capture(CSharpParseOptions options)
    {
        return new ManifestParseOptions
        {
            LanguageVersion = options.LanguageVersion.ToString(),
            DocumentationMode = options.DocumentationMode.ToString(),
            Kind = options.Kind.ToString(),
            PreprocessorSymbols = options.PreprocessorSymbolNames.OrderBy(s => s, StringComparer.Ordinal).ToList(),
            Features = options.Features
                .ToDictionary(kv => kv.Key, kv => kv.Value, StringComparer.Ordinal),
        };
    }

    public static CSharpParseOptions Restore(ManifestParseOptions captured)
    {
        var langVersion = Enum.Parse<LanguageVersion>(captured.LanguageVersion);
        return new CSharpParseOptions(
                languageVersion: langVersion,
                documentationMode: Enum.Parse<DocumentationMode>(captured.DocumentationMode),
                kind: Enum.Parse<SourceCodeKind>(captured.Kind))
            .WithPreprocessorSymbols(captured.PreprocessorSymbols)
            .WithFeatures(captured.Features);
    }
}
