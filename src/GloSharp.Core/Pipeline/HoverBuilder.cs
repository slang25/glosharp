using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace GloSharp.Core;

/// <summary>
/// Turns a token into a hover: which symbol it stands for, and how that symbol is displayed
/// (signature parts, kind prefix, overload count, doc comment, anonymous type annotations).
/// </summary>
internal static class HoverBuilder
{
    private static readonly SymbolDisplayFormat DisplayFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions:
            SymbolDisplayMemberOptions.IncludeType |
            SymbolDisplayMemberOptions.IncludeParameters |
            SymbolDisplayMemberOptions.IncludeContainingType,
        parameterOptions:
            SymbolDisplayParameterOptions.IncludeType |
            SymbolDisplayParameterOptions.IncludeName |
            SymbolDisplayParameterOptions.IncludeDefaultValue,
        localOptions: SymbolDisplayLocalOptions.IncludeType,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    /// <summary>
    /// Allow-list of tokens that carry their own symbol: identifiers (including contextual
    /// keywords such as <c>var</c>), predefined type keywords, <c>this</c>/<c>base</c> and the
    /// <c>new</c> of target-typed/anonymous object creation. Operators and punctuation never
    /// borrow the hover of an enclosing call or declaration.
    /// </summary>
    public static bool IsHoverableToken(SyntaxToken token)
    {
        if (token.IsKind(SyntaxKind.IdentifierToken))
            return true;

        return token.IsKeyword() && token.Parent is PredefinedTypeSyntax
            or ThisExpressionSyntax
            or BaseExpressionSyntax
            or ImplicitObjectCreationExpressionSyntax
            or AnonymousObjectCreationExpressionSyntax;
    }

    /// <summary>
    /// The hover for <paramref name="token"/>, placed at (<paramref name="line"/>,
    /// <paramref name="character"/>) of the processed code, or null when the token has no
    /// useful symbol.
    /// </summary>
    public static GloSharpHover? Build(SyntaxToken token, SemanticModel model, int line, int character, bool persistent)
    {
        if (!IsHoverableToken(token))
            return null;

        var symbol = ResolveTokenSymbol(token, model);
        if (symbol == null) return null;

        // The synthetic top-level-statements entry point is an implementation detail
        if (symbol is IMethodSymbol { Name: "<Main>$" })
            return null;

        // Unresolvable types would produce a misleading, info-free hover
        if (HasErrorType(symbol))
            return null;

        List<GloSharpDisplayPart> displayParts;
        string text;
        List<GloSharpTypeAnnotation>? typeAnnotations = null;

        if (symbol is IRangeVariableSymbol rangeVariable)
        {
            (displayParts, text) = BuildRangeVariableDisplay(rangeVariable, token, model);
        }
        else
        {
            var parts = symbol.ToDisplayParts(DisplayFormat);
            var prefix = GetSymbolPrefix(symbol);

            var formatter = AnonymousTypeFormatter.FindAnonymousType(symbol) != null ? new AnonymousTypeFormatter() : null;

            displayParts = PrefixParts(prefix);
            if (formatter != null)
            {
                displayParts.AddRange(formatter.TransformDisplayParts(parts, symbol));
            }
            else
            {
                foreach (var part in parts)
                    displayParts.Add(ToDisplayPart(part));
            }

            var rawDisplayString = symbol.ToDisplayString(DisplayFormat);
            var display = formatter != null ? formatter.TransformDisplayString(rawDisplayString) : rawDisplayString;
            text = prefix != null ? $"({prefix}) {display}" : display;
            typeAnnotations = formatter?.GetAnnotations();
        }

        int? overloadCount = null;
        if (symbol is IMethodSymbol method && method.ContainingType != null)
        {
            var overloads = method.ContainingType.GetMembers(method.Name)
                .OfType<IMethodSymbol>()
                .Count();
            if (overloads > 1)
            {
                overloadCount = overloads;
                text += overloads == 2 ? " (+ 1 overload)" : $" (+ {overloads - 1} overloads)";
            }
        }

        var docs = DocCommentFormatter.Extract(symbol, model.Compilation);

        return new GloSharpHover
        {
            Line = line,
            Character = character,
            Length = token.Text.Length,
            Text = text,
            Parts = displayParts,
            Docs = docs,
            SymbolKind = SymbolDisplayPartKindMapping.ToSymbolKindString(symbol),
            TargetText = token.Text,
            OverloadCount = overloadCount,
            TypeAnnotations = typeAnnotations,
            Persistent = persistent,
        };
    }

    private static ISymbol? ResolveTokenSymbol(SyntaxToken token, SemanticModel model)
    {
        var node = token.Parent;
        if (node == null) return null;

        // 'this' / 'base' show the type they refer to
        if (node is ThisExpressionSyntax or BaseExpressionSyntax)
            return model.GetTypeInfo(node).Type;

        var info = model.GetSymbolInfo(node);
        var symbol = info.Symbol ?? info.CandidateSymbols.FirstOrDefault();
        symbol ??= model.GetDeclaredSymbol(node);

        // Names that label something declared by their parent: anonymous type members
        // (new { Name = x }), named tuple elements ((Name: x, ...)), named arguments.
        if (symbol == null && node.Parent is NameEqualsSyntax or NameColonSyntax)
            symbol = model.GetDeclaredSymbol(node.Parent.Parent!);

        return symbol;
    }

    private static GloSharpDisplayPart ToDisplayPart(SymbolDisplayPart part) => new()
    {
        Kind = SymbolDisplayPartKindMapping.ToJsonKind(part.Kind),
        Text = part.ToString(),
    };

    private static List<GloSharpDisplayPart> PrefixParts(string? prefix)
    {
        var parts = new List<GloSharpDisplayPart>();
        if (prefix != null)
        {
            parts.Add(new GloSharpDisplayPart { Kind = "punctuation", Text = "(" });
            parts.Add(new GloSharpDisplayPart { Kind = "text", Text = prefix });
            parts.Add(new GloSharpDisplayPart { Kind = "punctuation", Text = ")" });
            parts.Add(new GloSharpDisplayPart { Kind = "space", Text = " " });
        }
        return parts;
    }

    /// <summary>
    /// Range variables (<c>from p in people</c>) have no type in their symbol; Roslyn's display
    /// renders them as "? p". Show "(range variable) T p" like Visual Studio does instead.
    /// </summary>
    private static (List<GloSharpDisplayPart> Parts, string Text) BuildRangeVariableDisplay(
        IRangeVariableSymbol rangeVariable, SyntaxToken token, SemanticModel model)
    {
        var type = GetRangeVariableType(rangeVariable, token, model);
        var parts = PrefixParts("range variable");
        var text = new StringBuilder("(range variable) ");

        if (type != null)
        {
            foreach (var part in type.ToDisplayParts(DisplayFormat))
                parts.Add(ToDisplayPart(part));
            parts.Add(new GloSharpDisplayPart { Kind = "space", Text = " " });
            text.Append(type.ToDisplayString(DisplayFormat)).Append(' ');
        }

        parts.Add(new GloSharpDisplayPart { Kind = "localName", Text = rangeVariable.Name });
        text.Append(rangeVariable.Name);
        return (parts, text.ToString());
    }

    private static ITypeSymbol? GetRangeVariableType(IRangeVariableSymbol rangeVariable, SyntaxToken token, SemanticModel model)
    {
        // A usage binds directly
        if (token.Parent is IdentifierNameSyntax usage)
        {
            var usageType = model.GetTypeInfo(usage).Type;
            if (usageType is { TypeKind: not TypeKind.Error })
                return usageType;
        }

        // Declaration: find a usage in the same query
        var query = token.Parent?.FirstAncestorOrSelf<QueryExpressionSyntax>();
        if (query != null)
        {
            foreach (var identifier in query.DescendantNodes().OfType<IdentifierNameSyntax>())
            {
                if (identifier.Identifier.ValueText != rangeVariable.Name) continue;
                if (!SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(identifier).Symbol, rangeVariable)) continue;
                var type = model.GetTypeInfo(identifier).Type;
                if (type is { TypeKind: not TypeKind.Error })
                    return type;
            }
        }

        // Unused: derive from the declaring clause
        return token.Parent switch
        {
            FromClauseSyntax { Type: { } explicitType } => model.GetTypeInfo(explicitType).Type,
            FromClauseSyntax from => GetElementType(model.GetTypeInfo(from.Expression).Type),
            JoinClauseSyntax { Type: { } explicitType } => model.GetTypeInfo(explicitType).Type,
            JoinClauseSyntax join => GetElementType(model.GetTypeInfo(join.InExpression).Type),
            LetClauseSyntax let => model.GetTypeInfo(let.Expression).Type,
            _ => null,
        };
    }

    private static ITypeSymbol? GetElementType(ITypeSymbol? type)
    {
        if (type == null) return null;
        if (type is IArrayTypeSymbol array) return array.ElementType;

        var enumerable = type.AllInterfaces
            .Prepend(type as INamedTypeSymbol)
            .OfType<INamedTypeSymbol>()
            .FirstOrDefault(i => i.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T);
        return enumerable?.TypeArguments[0];
    }

    private static bool HasErrorType(ISymbol symbol) => symbol switch
    {
        ILocalSymbol { Type.TypeKind: TypeKind.Error } => true,
        IParameterSymbol { Type.TypeKind: TypeKind.Error } => true,
        IFieldSymbol { Type.TypeKind: TypeKind.Error } => true,
        IPropertySymbol { Type.TypeKind: TypeKind.Error } => true,
        IMethodSymbol { ReturnType.TypeKind: TypeKind.Error } => true,
        IMethodSymbol { ContainingType.TypeKind: TypeKind.Error } => true,
        ITypeSymbol { TypeKind: TypeKind.Error } => true,
        _ => false,
    };

    private static string? GetSymbolPrefix(ISymbol symbol) => symbol switch
    {
        ILocalSymbol => "local variable",
        IParameterSymbol => "parameter",
        IRangeVariableSymbol => "range variable",
        IFieldSymbol f => f.IsConst ? "constant" : "field",
        IPropertySymbol => "property",
        IMethodSymbol m => m.IsExtensionMethod ? "extension" : "method",
        IEventSymbol => "event",
        INamedTypeSymbol nts => nts.TypeKind switch
        {
            TypeKind.Class => nts.IsRecord ? "record" : "class",
            TypeKind.Struct => nts.IsRecord ? "record struct" : "struct",
            TypeKind.Interface => "interface",
            TypeKind.Enum => "enum",
            TypeKind.Delegate => "delegate",
            _ => null,
        },
        INamespaceSymbol => "namespace",
        _ => null,
    };
}
