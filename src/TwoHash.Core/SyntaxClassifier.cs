using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Classification;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Host.Mef;
using Microsoft.CodeAnalysis.Text;

namespace TwoHash.Core;

public record ClassifiedToken(int Start, int Length, string Kind, string Text);

public class SyntaxClassifier
{
    public static async Task<List<ClassifiedToken>> ClassifyAsync(
        string sourceCode,
        CSharpCompilation compilation,
        SyntaxTree tree)
    {
        var host = MefHostServices.Create(MefHostServices.DefaultAssemblies);
        using var workspace = new AdhocWorkspace(host);

        var project = workspace.AddProject("TwohashClassification", LanguageNames.CSharp);
        project = project
            .WithCompilationOptions(compilation.Options)
            .WithParseOptions(tree.Options);

        foreach (var reference in compilation.References)
            project = project.AddMetadataReference(reference);

        // Add all syntax trees from the compilation (includes global usings)
        foreach (var syntaxTree in compilation.SyntaxTrees)
        {
            if (syntaxTree == tree) continue;
            var text = (await syntaxTree.GetTextAsync()).ToString();
            project = project.AddDocument(syntaxTree.FilePath ?? $"_aux_{syntaxTree.GetHashCode()}.cs", text).Project;
        }

        var document = project.AddDocument("snippet.cs", sourceCode);
        workspace.TryApplyChanges(document.Project.Solution);
        document = workspace.CurrentSolution.GetDocument(document.Id)!;

        var spans = await Classifier.GetClassifiedSpansAsync(
            document,
            TextSpan.FromBounds(0, sourceCode.Length));

        // Deduplicate overlapping spans — prefer semantic classifications over syntactic ones.
        // Roslyn returns both syntactic (e.g., "identifier") and semantic (e.g., "class name")
        // spans for the same token. Group by position and pick the most specific.
        var deduped = spans
            .GroupBy(s => (s.TextSpan.Start, s.TextSpan.Length))
            .Select(g => g.OrderByDescending(s => ClassificationPriority(s.ClassificationType)).First())
            .OrderBy(s => s.TextSpan.Start)
            .ToList();

        var tokens = new List<ClassifiedToken>();
        var lastEnd = 0;

        foreach (var span in deduped)
        {
            // Skip spans that overlap with previously consumed tokens
            if (span.TextSpan.Start < lastEnd)
                continue;

            // Fill gaps with unclassified text
            if (span.TextSpan.Start > lastEnd)
            {
                var gapText = sourceCode[lastEnd..span.TextSpan.Start];
                if (!string.IsNullOrEmpty(gapText))
                    tokens.Add(new ClassifiedToken(lastEnd, gapText.Length, "text", gapText));
            }

            var tokenText = sourceCode.Substring(span.TextSpan.Start, span.TextSpan.Length);
            var kind = MapClassificationType(span.ClassificationType);
            tokens.Add(new ClassifiedToken(span.TextSpan.Start, span.TextSpan.Length, kind, tokenText));
            lastEnd = span.TextSpan.End;
        }

        // Trailing text
        if (lastEnd < sourceCode.Length)
        {
            var trailing = sourceCode[lastEnd..];
            if (!string.IsNullOrEmpty(trailing))
                tokens.Add(new ClassifiedToken(lastEnd, trailing.Length, "text", trailing));
        }

        return tokens;
    }

    private static int ClassificationPriority(string classificationType)
    {
        // Higher = preferred. Semantic classifications are more specific than syntactic ones.
        if (classificationType is ClassificationTypeNames.Keyword or ClassificationTypeNames.ControlKeyword
            or ClassificationTypeNames.StringLiteral or ClassificationTypeNames.VerbatimStringLiteral
            or ClassificationTypeNames.NumericLiteral or ClassificationTypeNames.Comment
            or ClassificationTypeNames.Operator or ClassificationTypeNames.Punctuation
            or ClassificationTypeNames.PreprocessorKeyword)
            return 1; // syntactic — still valuable

        if (classificationType is ClassificationTypeNames.ClassName or ClassificationTypeNames.StructName
            or ClassificationTypeNames.InterfaceName or ClassificationTypeNames.EnumName
            or ClassificationTypeNames.DelegateName or ClassificationTypeNames.TypeParameterName
            or ClassificationTypeNames.RecordClassName or ClassificationTypeNames.RecordStructName
            or ClassificationTypeNames.MethodName or ClassificationTypeNames.ExtensionMethodName
            or ClassificationTypeNames.PropertyName or ClassificationTypeNames.FieldName
            or ClassificationTypeNames.ConstantName or ClassificationTypeNames.EnumMemberName
            or ClassificationTypeNames.EventName or ClassificationTypeNames.LocalName
            or ClassificationTypeNames.ParameterName or ClassificationTypeNames.NamespaceName
            or ClassificationTypeNames.LabelName)
            return 2; // semantic — highest priority

        if (classificationType == ClassificationTypeNames.WhiteSpace)
            return 0;

        return 0; // everything else (text, identifiers, etc.)
    }

    public static string MapClassificationType(string classificationType) => classificationType switch
    {
        ClassificationTypeNames.Keyword => "keyword",
        ClassificationTypeNames.ControlKeyword => "keyword",
        ClassificationTypeNames.ClassName => "className",
        ClassificationTypeNames.RecordClassName => "className",
        ClassificationTypeNames.StructName => "structName",
        ClassificationTypeNames.RecordStructName => "structName",
        ClassificationTypeNames.InterfaceName => "interfaceName",
        ClassificationTypeNames.EnumName => "enumName",
        ClassificationTypeNames.DelegateName => "delegateName",
        ClassificationTypeNames.TypeParameterName => "typeParameterName",
        ClassificationTypeNames.MethodName => "methodName",
        ClassificationTypeNames.ExtensionMethodName => "methodName",
        ClassificationTypeNames.PropertyName => "propertyName",
        ClassificationTypeNames.FieldName => "fieldName",
        ClassificationTypeNames.ConstantName => "fieldName",
        ClassificationTypeNames.EnumMemberName => "enumName",
        ClassificationTypeNames.EventName => "eventName",
        ClassificationTypeNames.LocalName => "localName",
        ClassificationTypeNames.ParameterName => "parameterName",
        ClassificationTypeNames.NamespaceName => "namespaceName",
        ClassificationTypeNames.StringLiteral => "string",
        ClassificationTypeNames.VerbatimStringLiteral => "string",
        ClassificationTypeNames.StringEscapeCharacter => "string",
        ClassificationTypeNames.NumericLiteral => "number",
        ClassificationTypeNames.Comment => "comment",
        ClassificationTypeNames.XmlDocCommentComment => "comment",
        ClassificationTypeNames.XmlDocCommentDelimiter => "comment",
        ClassificationTypeNames.XmlDocCommentText => "comment",
        ClassificationTypeNames.XmlDocCommentName => "comment",
        ClassificationTypeNames.XmlDocCommentAttributeName => "comment",
        ClassificationTypeNames.XmlDocCommentAttributeValue => "comment",
        ClassificationTypeNames.XmlDocCommentAttributeQuotes => "comment",
        ClassificationTypeNames.Operator => "operator",
        ClassificationTypeNames.Punctuation => "punctuation",
        ClassificationTypeNames.PreprocessorKeyword => "keyword",
        ClassificationTypeNames.PreprocessorText => "text",
        ClassificationTypeNames.WhiteSpace => "whitespace",
        ClassificationTypeNames.Text => "text",
        ClassificationTypeNames.StaticSymbol => "text", // modifier, not a color
        ClassificationTypeNames.LabelName => "localName",
        _ => "text",
    };
}
