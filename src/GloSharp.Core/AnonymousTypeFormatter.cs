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
        // Check containing type first — for properties/methods on anonymous types
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
        {
            var elementDisplay = FormatPropertyType(array.ElementType);
            return $"{elementDisplay}[]";
        }

        if (type is INamedTypeSymbol generic && generic.IsGenericType)
        {
            var hasAnonymousArg = generic.TypeArguments.Any(a => FindAnonymousTypeInType(a) != null);
            if (hasAnonymousArg)
            {
                var name = generic.Name;
                var args = generic.TypeArguments.Select(FormatPropertyType);
                return $"{name}<{string.Join(", ", args)}>";
            }
        }

        return type.ToDisplayString(PropertyTypeFormat);
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

                // Skip the entire anonymous type inline expansion that Roslyn generates.
                // Roslyn renders: <anonymous type: Type1 Prop1, Type2 Prop2>
                // We want to replace the whole thing with the placeholder.
                if (i + 1 < parts.Count && parts[i + 1].ToString() == " ")
                {
                    // This is the className part followed by generic expansion — just emit placeholder
                    result.Add(new GloSharpDisplayPart { Kind = "className", Text = placeholder });
                }
                else
                {
                    result.Add(new GloSharpDisplayPart { Kind = "className", Text = placeholder });
                }
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
        return _annotations.Count > 0 ? _annotations.ToList() : null;
    }
}
