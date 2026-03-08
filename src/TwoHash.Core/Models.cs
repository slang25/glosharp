using System.Text.Json.Serialization;

namespace TwoHash.Core;

public class TwohashResult
{
    public required string Code { get; init; }
    public required string Original { get; init; }
    public string Lang { get; init; } = "csharp";
    public required List<TwohashHover> Hovers { get; init; }
    public required List<TwohashError> Errors { get; init; }
    public List<object> Completions { get; init; } = [];
    public List<object> Highlights { get; init; } = [];
    public List<object> Hidden { get; init; } = [];
    public required TwohashMeta Meta { get; init; }
}

public class TwohashHover
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required int Length { get; init; }
    public required string Text { get; init; }
    public required List<TwohashDisplayPart> Parts { get; init; }
    public string? Docs { get; init; }
    public required string SymbolKind { get; init; }
    public required string TargetText { get; init; }
    public int? OverloadCount { get; init; }
}

public class TwohashDisplayPart
{
    public required string Kind { get; init; }
    public required string Text { get; init; }
}

public class TwohashError
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required int Length { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public required bool Expected { get; init; }
}

public class TwohashMeta
{
    public required string TargetFramework { get; init; }
    public List<PackageReference> Packages { get; init; } = [];
    public required bool CompileSucceeded { get; init; }
}

public class PackageReference
{
    public required string Name { get; init; }
    public required string Version { get; init; }
}
