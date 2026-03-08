using Microsoft.CodeAnalysis;

namespace TwoHash.Core;

public static class SymbolDisplayPartKindMapping
{
    public static string ToJsonKind(SymbolDisplayPartKind kind) => kind switch
    {
        SymbolDisplayPartKind.Keyword => "keyword",
        SymbolDisplayPartKind.ClassName => "className",
        SymbolDisplayPartKind.StructName => "structName",
        SymbolDisplayPartKind.InterfaceName => "interfaceName",
        SymbolDisplayPartKind.EnumName => "enumName",
        SymbolDisplayPartKind.DelegateName => "delegateName",
        SymbolDisplayPartKind.MethodName => "methodName",
        SymbolDisplayPartKind.PropertyName => "propertyName",
        SymbolDisplayPartKind.FieldName => "fieldName",
        SymbolDisplayPartKind.EventName => "eventName",
        SymbolDisplayPartKind.LocalName => "localName",
        SymbolDisplayPartKind.ParameterName => "parameterName",
        SymbolDisplayPartKind.NamespaceName => "namespaceName",
        SymbolDisplayPartKind.Punctuation => "punctuation",
        SymbolDisplayPartKind.Operator => "operator",
        SymbolDisplayPartKind.Space => "space",
        SymbolDisplayPartKind.Text => "text",
        SymbolDisplayPartKind.LineBreak => "lineBreak",
        SymbolDisplayPartKind.TypeParameterName => "typeParameterName",
        SymbolDisplayPartKind.RecordClassName => "className",
        SymbolDisplayPartKind.RecordStructName => "structName",
        _ => "text",
    };

    public static string ToSymbolKindString(ISymbol symbol) => symbol.Kind switch
    {
        Microsoft.CodeAnalysis.SymbolKind.Local => "Local",
        Microsoft.CodeAnalysis.SymbolKind.Parameter => "Parameter",
        Microsoft.CodeAnalysis.SymbolKind.Field => "Field",
        Microsoft.CodeAnalysis.SymbolKind.Property => "Property",
        Microsoft.CodeAnalysis.SymbolKind.Method => "Method",
        Microsoft.CodeAnalysis.SymbolKind.NamedType => symbol is INamedTypeSymbol nts ? nts.TypeKind switch
        {
            TypeKind.Class => "Class",
            TypeKind.Struct => "Struct",
            TypeKind.Interface => "Interface",
            TypeKind.Enum => "Enum",
            TypeKind.Delegate => "Delegate",
            _ => "Type",
        } : "Type",
        Microsoft.CodeAnalysis.SymbolKind.Namespace => "Namespace",
        Microsoft.CodeAnalysis.SymbolKind.Event => "Event",
        _ => "Unknown",
    };
}
