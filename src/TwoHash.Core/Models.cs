using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TwoHash.Core;

/// <summary>
/// Extended result that includes compilation context for downstream use (e.g., syntax classification).
/// Not serialized to JSON — use <see cref="TwohashResult"/> for the JSON contract.
/// </summary>
public class TwohashProcessResult
{
    public required TwohashResult Result { get; init; }
    public required CSharpCompilation Compilation { get; init; }
    public required SyntaxTree SyntaxTree { get; init; }
}

public class TwohashResult
{
    public required string Code { get; init; }
    public required string Original { get; init; }
    public string Lang { get; init; } = "csharp";
    public required List<TwohashHover> Hovers { get; init; }
    public required List<TwohashError> Errors { get; init; }
    public List<TwohashCompletion> Completions { get; init; } = [];
    public List<TwohashHighlight> Highlights { get; init; } = [];
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
    public TwohashDocComment? Docs { get; init; }
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
    public string? Sdk { get; init; }
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
    public string? Complog { get; init; }
}

public class TwohashCompletion
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required List<TwohashCompletionItem> Items { get; init; }
}

public class TwohashCompletionItem
{
    public required string Label { get; init; }
    public required string Kind { get; init; }
    public string? Detail { get; init; }
}

public class PackageReference
{
    public required string Name { get; init; }
    public required string Version { get; init; }
}

public class TwohashHighlight
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required int Length { get; init; }
    public required string Kind { get; init; }
}

public class TwohashDocComment
{
    public string? Summary { get; init; }
    public List<TwohashDocParam> Params { get; init; } = [];
    public string? Returns { get; init; }
    public string? Remarks { get; init; }
    public List<string> Examples { get; init; } = [];
    public List<TwohashDocException> Exceptions { get; init; } = [];
}

public class TwohashDocParam
{
    public required string Name { get; init; }
    public required string Text { get; init; }
}

public class TwohashDocException
{
    public required string Type { get; init; }
    public required string Text { get; init; }
}
