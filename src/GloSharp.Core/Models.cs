using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>
/// Extended result that includes compilation context for downstream use (e.g., syntax classification).
/// Not serialized to JSON — use <see cref="GloSharpResult"/> for the JSON contract.
/// </summary>
public class GloSharpProcessResult
{
    public required GloSharpResult Result { get; init; }
    public required CSharpCompilation Compilation { get; init; }
    public required SyntaxTree SyntaxTree { get; init; }
}

public class GloSharpResult
{
    public required string Code { get; init; }
    public required string Original { get; init; }
    public string Lang { get; init; } = "csharp";
    public required List<GloSharpHover> Hovers { get; init; }
    public required List<GloSharpError> Errors { get; init; }
    public List<GloSharpCompletion> Completions { get; init; } = [];
    public List<GloSharpHighlight> Highlights { get; init; } = [];
    public List<object> Hidden { get; init; } = [];
    public required GloSharpMeta Meta { get; init; }
}

public class GloSharpHover
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required int Length { get; init; }
    public required string Text { get; init; }
    public required List<GloSharpDisplayPart> Parts { get; init; }
    public GloSharpDocComment? Docs { get; init; }
    public required string SymbolKind { get; init; }
    public required string TargetText { get; init; }
    public int? OverloadCount { get; init; }
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool Persistent { get; init; }
}

public class GloSharpDisplayPart
{
    public required string Kind { get; init; }
    public required string Text { get; init; }
}

public class GloSharpError
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required int Length { get; init; }
    public int? EndLine { get; init; }
    public int? EndCharacter { get; init; }
    public required string Code { get; init; }
    public required string Message { get; init; }
    public required string Severity { get; init; }
    public required bool Expected { get; init; }
}

public class GloSharpMeta
{
    public required string TargetFramework { get; init; }
    public List<PackageReference> Packages { get; init; } = [];
    public required bool CompileSucceeded { get; init; }
    public string? Sdk { get; init; }
    public string? LangVersion { get; init; }
    public string? Nullable { get; init; }
    public string? Complog { get; init; }
}

public class GloSharpCompletion
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required List<GloSharpCompletionItem> Items { get; init; }
}

public class GloSharpCompletionItem
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

public class GloSharpHighlight
{
    public required int Line { get; init; }
    public required int Character { get; init; }
    public required int Length { get; init; }
    public required string Kind { get; init; }
}

public class GloSharpDocComment
{
    public string? Summary { get; init; }
    public List<GloSharpDocParam> Params { get; init; } = [];
    public string? Returns { get; init; }
    public string? Remarks { get; init; }
    public List<string> Examples { get; init; } = [];
    public List<GloSharpDocException> Exceptions { get; init; } = [];
}

public class GloSharpDocParam
{
    public required string Name { get; init; }
    public required string Text { get; init; }
}

public class GloSharpDocException
{
    public required string Type { get; init; }
    public required string Text { get; init; }
}
