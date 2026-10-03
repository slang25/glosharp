using System.Text.Json.Serialization;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>
/// Extended result that includes compilation context for downstream use (e.g., syntax classification).
/// Not serialized to JSON — use <see cref="GloSharpResult"/> for the JSON contract.
/// </summary>
/// <remarks>
/// On a result-cache hit the compilation is not built up front: <see cref="Compilation"/> and
/// <see cref="SyntaxTree"/> resolve references and compile lazily, on first access.
/// </remarks>
public class GloSharpProcessResult
{
    private readonly Lazy<(CSharpCompilation Compilation, SyntaxTree SyntaxTree)> _context;

    public GloSharpProcessResult(GloSharpResult result, CSharpCompilation compilation, SyntaxTree syntaxTree)
    {
        Result = result;
        _context = new Lazy<(CSharpCompilation, SyntaxTree)>((compilation, syntaxTree));
    }

    public GloSharpProcessResult(GloSharpResult result, Func<(CSharpCompilation Compilation, SyntaxTree SyntaxTree)> contextFactory)
    {
        Result = result;
        _context = new Lazy<(CSharpCompilation, SyntaxTree)>(contextFactory, LazyThreadSafetyMode.ExecutionAndPublication);
    }

    public GloSharpResult Result { get; }

    /// <summary>True when <see cref="Result"/> came from the on-disk result cache.</summary>
    public bool FromCache { get; init; }

    public CSharpCompilation Compilation => _context.Value.Compilation;
    public SyntaxTree SyntaxTree => _context.Value.SyntaxTree;
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
    public List<GloSharpTag> Tags { get; init; } = [];
    public List<GloSharpHiddenRange> Hidden { get; init; } = [];

    /// <summary>
    /// Error-severity diagnostics located in hidden (cut/region) code. They cannot be placed in
    /// <see cref="Code"/>, so <c>line</c> is -1; use <c>sourceLine</c>/<c>sourceCharacter</c>.
    /// </summary>
    public List<GloSharpError> HiddenErrors { get; init; } = [];
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
    public List<GloSharpTypeAnnotation>? TypeAnnotations { get; init; }
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

    /// <summary>0-based line in the original input text (before directive/marker/cut/region stripping).</summary>
    public int? SourceLine { get; init; }

    /// <summary>0-based UTF-16 column in the original input line.</summary>
    public int? SourceCharacter { get; init; }
}

/// <summary>
/// A run of input lines hidden from <see cref="GloSharpResult.Code"/> (cut markers, regions).
/// </summary>
public class GloSharpHiddenRange
{
    /// <summary>The processed line the hidden block sits before (equal to the line count when it is at the end).</summary>
    public required int Line { get; init; }

    /// <summary>First hidden line, 0-based, in the original input text.</summary>
    public required int SourceStartLine { get; init; }

    /// <summary>Last hidden line (inclusive), 0-based, in the original input text.</summary>
    public required int SourceEndLine { get; init; }
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

    /// <summary>
    /// Non-fatal problems the author should know about (failed package restore, a caret that
    /// points past the end of its line, an empty completion list, ...). Always present.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
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

public class GloSharpTag
{
    public required string Name { get; init; }
    public required string Text { get; init; }
    public required int Line { get; init; }
}

public class GloSharpTypeAnnotation
{
    public required string Name { get; init; }
    public required string Expansion { get; init; }
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
