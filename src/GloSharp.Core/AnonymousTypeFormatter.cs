using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

public class AnonymousTypeFormatter
{
    private readonly Dictionary<INamedTypeSymbol, string> _placeholders = new(SymbolEqualityComparer.Default);
    private readonly List<GloSharpTypeAnnotation> _annotations = [];
    private int _nextPlaceholder;

    private static readonly SymbolDisplayFormat PropertyTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        miscellaneousOptions:
            SymbolDisplayMiscellaneousOptions.UseSpecialTypes |
            SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier
    );

    public static INamedTypeSymbol? FindAnonymousType(ISymbol symbol)
    {
        // Check if the symbol itself is an anonymous type (e.g., hovering 'var' resolves to the type)
        if (symbol is INamedTypeSymbol { IsAnonymousType: true } selfAnon)
            return selfAnon;

        // Check if the symbol is a type that contains an anonymous type (e.g., anonymous type array)
        if (symbol is ITypeSymbol typeSymbol)
            return FindAnonymousTypeInType(typeSymbol);

        // Check containing type — for properties/methods on anonymous types
        if (symbol.ContainingType is { IsAnonymousType: true } containingAnon)
            return containingAnon;

        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol param => param.Type,
            IPropertySymbol prop => prop.Type,
            IMethodSymbol method => method.ReturnType,
            IFieldSymbol field => field.Type,
            _ => null,
        };

        return FindAnonymousTypeInType(type);
    }

    private static INamedTypeSymbol? FindAnonymousTypeInType(ITypeSymbol? type)
    {
        if (type is INamedTypeSymbol named && named.IsAnonymousType)
            return named;

        if (type is IArrayTypeSymbol array)
            return FindAnonymousTypeInType(array.ElementType);

        if (type is INamedTypeSymbol generic && generic.IsGenericType)
        {
            foreach (var arg in generic.TypeArguments)
            {
                var found = FindAnonymousTypeInType(arg);
                if (found != null) return found;
            }
        }

        return null;
    }

    public string GetOrAssignPlaceholder(INamedTypeSymbol anonymousType)
    {
        if (_placeholders.TryGetValue(anonymousType, out var existing))
            return existing;

        var placeholder = $"'{(char)('a' + _nextPlaceholder)}";
        _nextPlaceholder++;
        _placeholders[anonymousType] = placeholder;

        var expansion = BuildExpansion(anonymousType);
        _annotations.Add(new GloSharpTypeAnnotation
        {
            Name = placeholder,
            Expansion = expansion,
        });

        return placeholder;
    }

    private string BuildExpansion(INamedTypeSymbol anonymousType)
    {
        var properties = anonymousType.GetMembers()
            .OfType<IPropertySymbol>()
            .Where(p => p.CanBeReferencedByName);

        var parts = new List<string>();
        foreach (var prop in properties)
        {
            var typeDisplay = FormatPropertyType(prop.Type);
            parts.Add($"{typeDisplay} {prop.Name}");
        }

        return $"new {{ {string.Join(", ", parts)} }}";
    }

    private string FormatPropertyType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsAnonymousType)
            return GetOrAssignPlaceholder(named);

        if (type is IArrayTypeSymbol array)
            return FormatArrayType(array);

        if (type is INamedTypeSymbol generic && generic.IsGenericType)
        {
            var hasAnonymousArg = generic.TypeArguments.Any(a => FindAnonymousTypeInType(a) != null);
            if (hasAnonymousArg)
                return FormatNamedType(generic);
        }

        return type.ToDisplayString(PropertyTypeFormat);
    }

    private string FormatArrayType(IArrayTypeSymbol array)
    {
        var elementDisplay = FormatPropertyType(array.ElementType);
        var rankSpecifier = "[" + new string(',', array.Rank - 1) + "]";
        var nullableSuffix = array.NullableAnnotation == NullableAnnotation.Annotated ? "?" : string.Empty;
        return $"{elementDisplay}{rankSpecifier}{nullableSuffix}";
    }

    private string FormatNamedType(INamedTypeSymbol named)
    {
        var containingTypePrefix = named.ContainingType is null
            ? string.Empty
            : $"{FormatNamedType(named.ContainingType)}.";

        var typeArguments = named.TypeArguments.Select(FormatPropertyType);
        var nullableSuffix = named.NullableAnnotation == NullableAnnotation.Annotated ? "?" : string.Empty;
        return $"{containingTypePrefix}{named.Name}<{string.Join(", ", typeArguments)}>{nullableSuffix}";
    }

    public List<GloSharpDisplayPart> TransformDisplayParts(
        IReadOnlyList<SymbolDisplayPart> parts,
        ISymbol symbol)
    {
        CollectAnonymousTypes(symbol);

        var result = new List<GloSharpDisplayPart>();

        for (var i = 0; i < parts.Count; i++)
        {
            var part = parts[i];

            if (part.Symbol is INamedTypeSymbol named && named.IsAnonymousType)
            {
                var placeholder = GetOrAssignPlaceholder(named);

                // Replace the display part associated with the anonymous type symbol
                // with its placeholder and continue processing the remaining parts.
                result.Add(new GloSharpDisplayPart { Kind = "className", Text = placeholder });
                continue;
            }

            result.Add(new GloSharpDisplayPart
            {
                Kind = SymbolDisplayPartKindMapping.ToJsonKind(part.Kind),
                Text = part.ToString(),
            });
        }

        return result;
    }

    private void CollectAnonymousTypes(ISymbol symbol)
    {
        // If the symbol itself is a type, collect from it directly
        if (symbol is ITypeSymbol typeSymbol)
        {
            CollectAnonymousTypesFromType(typeSymbol);
            return;
        }

        if (symbol.ContainingType is { IsAnonymousType: true } containingAnon)
            GetOrAssignPlaceholder(containingAnon);

        var type = symbol switch
        {
            ILocalSymbol local => local.Type,
            IParameterSymbol param => param.Type,
            IPropertySymbol prop => prop.Type,
            IMethodSymbol method => method.ReturnType,
            IFieldSymbol field => field.Type,
            _ => null,
        };

        if (type != null)
            CollectAnonymousTypesFromType(type);
    }

    private void CollectAnonymousTypesFromType(ITypeSymbol type)
    {
        if (type is INamedTypeSymbol named && named.IsAnonymousType)
        {
            GetOrAssignPlaceholder(named);
            return;
        }

        if (type is IArrayTypeSymbol array)
        {
            CollectAnonymousTypesFromType(array.ElementType);
            return;
        }

        if (type is INamedTypeSymbol generic && generic.IsGenericType)
        {
            foreach (var arg in generic.TypeArguments)
                CollectAnonymousTypesFromType(arg);
        }
    }

    public string TransformDisplayString(string displayString)
    {
        var result = displayString;
        foreach (var (type, placeholder) in _placeholders)
        {
            var roslynName = type.ToDisplayString(PropertyTypeFormat);
            if (result.Contains(roslynName))
                result = result.Replace(roslynName, placeholder);
        }
        return result;
    }

    public List<GloSharpTypeAnnotation>? GetAnnotations()
    {
        return _annotations.Count > 0
            ? _annotations.OrderBy(a => a.Name).ToList()
            : null;
    }
}
