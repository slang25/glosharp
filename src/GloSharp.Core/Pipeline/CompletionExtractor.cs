using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Completion;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GloSharp.Core;

/// <summary>
/// Answers the snippet's <c>^|</c> completion queries with Roslyn's
/// <see cref="CompletionService"/>, filtered and ordered the way an editor would show them.
/// </summary>
internal static class CompletionExtractor
{
    public static async Task<List<GloSharpCompletion>> ExtractAsync(
        Snippet snippet,
        SnippetCompilation compilation,
        List<string> warnings)
    {
        var markers = snippet.Markers;
        var completions = new List<GloSharpCompletion>();
        if (markers.CompletionQueries.Count == 0)
            return completions;

        var lines = snippet.CompilationLines;
        var compilationCode = markers.CompilationCode;

        // The completion service needs a workspace document, built from the same options,
        // references and global usings as the compilation
        using var workspace = new AdhocWorkspace(SyntaxClassifier.Host);
        var project = workspace.AddProject("GloSharpCompletion", LanguageNames.CSharp)
            .WithCompilationOptions(compilation.Compilation.Options)
            .WithParseOptions(compilation.Tree.Options)
            .AddMetadataReferences(compilation.Context.References);
        project = project.AddDocument(CompilationBuilder.GlobalUsingsPath, compilation.GlobalUsings).Project;
        var document = project.AddDocument("snippet.cs", compilationCode);
        workspace.TryApplyChanges(document.Project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var completionService = CompletionService.GetService(document);
        if (completionService == null) return completions;
        var semanticModel = await document.GetSemanticModelAsync();
        var syntaxRoot = await document.GetSyntaxRootAsync();

        foreach (var query in markers.CompletionQueries)
        {
            var sourceLine = snippet.ProcessedToSourceLine(query.OriginalLine);
            var markerLine = snippet.ToSourceLine(query.MarkerInputLine);
            var compLine = snippet.ProcessedToCompilationLine(query.OriginalLine);

            // A completion caret may sit just after the last character (e.g. after "Console.")
            if (query.Column > lines.GetLineContentLength(compLine))
            {
                warnings.Add(
                    $"Line {markerLine + 1}: the ^| marker points at column {query.Column + 1}, past the end of line {sourceLine + 1}; the completion request was skipped.");
                continue;
            }

            var position = lines.GetLineStart(compLine) + query.Column;
            var completionList = await completionService.GetCompletionsAsync(document, position);

            var items = new List<GloSharpCompletionItem>();
            var receiverType = GetMemberAccessReceiverType(syntaxRoot, semanticModel, position);
            if (completionList != null)
            {
                // Filter by the identifier already typed before the caret, as an editor would
                var span = completionList.Span;
                var prefix = span.Start <= position && span.Start >= 0
                    ? compilationCode[span.Start..position]
                    : "";

                // Roslyn's item order is not stable between runs for items that sort equally, and
                // the dedupe below keeps the first of each label — so sort deterministically, as an
                // editor would (sort text, then label), preferring an instance member over an
                // extension method of the same name.
                var ordered = completionList.ItemsList
                    .OrderBy(i => i.SortText, StringComparer.Ordinal)
                    .ThenBy(i => i.DisplayText, StringComparer.Ordinal)
                    .ThenBy(i => i.Tags.Contains("ExtensionMethod") ? 1 : 0)
                    .ThenBy(i => string.Join(",", i.Tags), StringComparer.Ordinal)
                    .ThenBy(i => i.InlineDescription, StringComparer.Ordinal);

                var seen = new HashSet<string>(StringComparer.Ordinal);
                foreach (var item in ordered)
                {
                    if (prefix.Length > 0 && !item.FilterText.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                        continue;

                    // Overloads and generic/non-generic variants share a label: list it once
                    if (!seen.Add(item.DisplayText))
                        continue;

                    items.Add(new GloSharpCompletionItem
                    {
                        Label = item.DisplayText,
                        Kind = GetCompletionKind(item, receiverType, semanticModel!, position),
                        Detail = item.InlineDescription.Length > 0 ? item.InlineDescription : null,
                    });
                }
            }

            if (items.Count == 0)
            {
                warnings.Add(
                    $"Line {markerLine + 1}: the ^| marker (line {sourceLine + 1}, column {query.Column + 1}) produced no completions.");
            }

            completions.Add(new GloSharpCompletion
            {
                Line = query.OriginalLine,
                Character = query.Column,
                Items = items,
            });
        }

        return completions;
    }

    /// <summary>The type of <c>x</c> when completing a member name after <c>x.</c>.</summary>
    private static ITypeSymbol? GetMemberAccessReceiverType(SyntaxNode? root, SemanticModel? model, int position)
    {
        if (root == null || model == null) return null;
        var token = root.FindToken(position);
        if (token.IsKind(SyntaxKind.DotToken) || token.SpanStart >= position)
            token = token.IsKind(SyntaxKind.DotToken) ? token : token.GetPreviousToken();
        return token.Parent is MemberAccessExpressionSyntax access && access.OperatorToken == token
            ? model.GetTypeInfo(access.Expression).Type
            : null;
    }

    /// <summary>
    /// The item's symbol kind. Roslyn merges an instance method and an extension method of the
    /// same name into one item whose tags come from whichever symbol it saw first — which varies
    /// between runs — so for member access decide from the receiver type's own members.
    /// </summary>
    private static string GetCompletionKind(CompletionItem item, ITypeSymbol? receiverType, SemanticModel model, int position)
    {
        var kind = item.Tags.FirstOrDefault() ?? "Unknown";
        if (receiverType == null || kind is not ("Method" or "ExtensionMethod"))
            return kind;

        var isInstanceMember = model
            .LookupSymbols(position, receiverType, item.DisplayText, includeReducedExtensionMethods: false)
            .Any(s => s.Kind == SymbolKind.Method);
        return isInstanceMember ? "Method" : "ExtensionMethod";
    }
}
