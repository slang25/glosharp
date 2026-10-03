using System.Text;
using System.Text.RegularExpressions;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <summary>
/// Turns a symbol's XML documentation into the structured <see cref="GloSharpDocComment"/>.
/// </summary>
internal static partial class DocCommentFormatter
{
    private const int MaxInheritDepth = 8;

    // Type names in crefs: "List{T}" → "List<T>"
    private static readonly SymbolDisplayFormat CrefTypeFormat = new(
        globalNamespaceStyle: SymbolDisplayGlobalNamespaceStyle.Omitted,
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters);

    [GeneratedRegex(@"\s+")]
    private static partial Regex WhitespacePattern();

    [GeneratedRegex(@"``?\d+")]
    private static partial Regex ArityPattern();

    public static GloSharpDocComment? Extract(ISymbol symbol, Compilation? compilation)
    {
        var root = LoadDocument(symbol, compilation);
        if (root == null) return null;

        var ctx = new Context(compilation);

        var summary = Text(root.Element("summary"), ctx);
        var returns = Text(root.Element("returns") ?? root.Element("value"), ctx);
        var remarks = Text(root.Element("remarks"), ctx);

        var parameters = root.Elements("param")
            .Select(e => new GloSharpDocParam
            {
                Name = e.Attribute("name")?.Value ?? "",
                Text = Text(e, ctx) ?? "",
            })
            .Where(p => !string.IsNullOrEmpty(p.Name))
            .ToList();

        var examples = root.Elements("example")
            .Select(e => Text(e, ctx))
            .OfType<string>()
            .ToList();

        var exceptions = root.Elements("exception")
            .Select(e => new GloSharpDocException
            {
                Type = FormatCref(e.Attribute("cref")?.Value ?? "", ctx),
                Text = Text(e, ctx) ?? "",
            })
            .Where(e => !string.IsNullOrEmpty(e.Type))
            .ToList();

        if (summary == null && returns == null && remarks == null
            && parameters.Count == 0 && examples.Count == 0 && exceptions.Count == 0)
            return null;

        return new GloSharpDocComment
        {
            Summary = summary,
            Params = parameters,
            Returns = returns,
            Remarks = remarks,
            Examples = examples,
            Exceptions = exceptions,
        };
    }

    /// <summary>
    /// Loads the symbol's documentation as a single root element, expanding
    /// <c>&lt;inheritdoc/&gt;</c> from the overridden/implemented member (or its <c>cref</c>).
    /// Elements written locally win over inherited ones.
    /// </summary>
    private static XElement? LoadDocument(ISymbol symbol, Compilation? compilation, int depth = 0)
    {
        var xml = symbol.GetDocumentationCommentXml();
        if (string.IsNullOrWhiteSpace(xml)) return null;

        XElement root;
        try
        {
            root = XElement.Parse(xml);
        }
        catch (System.Xml.XmlException)
        {
            // Some providers return a fragment with several top-level elements
            try { root = XElement.Parse($"<member>{xml}</member>"); }
            catch (System.Xml.XmlException) { return null; }
        }

        if (root.Name.LocalName != "member" && root.Element("member") is { } member)
            root = member;

        var inheritDoc = root.Element("inheritdoc");
        if (inheritDoc == null)
            return root;

        inheritDoc.Remove();
        if (depth >= MaxInheritDepth)
            return root;

        var source = ResolveInheritSource(symbol, inheritDoc.Attribute("cref")?.Value, compilation);
        var inherited = source != null && !SymbolEqualityComparer.Default.Equals(source, symbol)
            ? LoadDocument(source, compilation, depth + 1)
            : null;
        if (inherited == null)
            return root;

        var localNames = root.Elements().Select(e => e.Name.LocalName).ToHashSet();
        var localParams = root.Elements("param").Select(e => e.Attribute("name")?.Value).ToHashSet();
        foreach (var element in inherited.Elements())
        {
            var name = element.Name.LocalName;
            if (name == "param")
            {
                if (!localParams.Contains(element.Attribute("name")?.Value))
                    root.Add(new XElement(element));
            }
            else if (!localNames.Contains(name))
            {
                root.Add(new XElement(element));
            }
        }

        return root;
    }

    private static ISymbol? ResolveInheritSource(ISymbol symbol, string? cref, Compilation? compilation)
    {
        if (!string.IsNullOrEmpty(cref))
        {
            return compilation != null
                ? DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, compilation)
                : null;
        }

        ISymbol? overridden = symbol switch
        {
            IMethodSymbol m => m.OverriddenMethod,
            IPropertySymbol p => p.OverriddenProperty,
            IEventSymbol e => e.OverriddenEvent,
            INamedTypeSymbol t => t.BaseType is { SpecialType: not SpecialType.System_Object } baseType
                ? baseType
                : t.Interfaces.FirstOrDefault(),
            _ => null,
        };
        if (overridden != null)
            return overridden;

        // Implicit or explicit interface implementation
        var containingType = symbol.ContainingType;
        if (containingType == null)
            return null;

        foreach (var iface in containingType.AllInterfaces)
        {
            // Explicit implementations are named "Namespace.IFoo.Bar"
            var name = symbol.Name[(symbol.Name.LastIndexOf('.') + 1)..];
            foreach (var member in iface.GetMembers(name))
            {
                var impl = containingType.FindImplementationForInterfaceMember(member);
                if (SymbolEqualityComparer.Default.Equals(impl, symbol))
                    return member;
            }
        }

        return null;
    }

    private static string? Text(XElement? element, Context ctx)
    {
        if (element == null) return null;

        var sb = new StringBuilder();
        AppendNodes(sb, element.Nodes(), ctx);
        var text = WhitespacePattern().Replace(sb.ToString(), " ").Trim();
        return string.IsNullOrEmpty(text) ? null : text;
    }

    private static void AppendNodes(StringBuilder sb, IEnumerable<XNode> nodes, Context ctx)
    {
        foreach (var node in nodes)
        {
            switch (node)
            {
                case XText text:
                    sb.Append(text.Value);
                    break;
                case XElement el:
                    AppendElement(sb, el, ctx);
                    break;
            }
        }
    }

    private static void AppendElement(StringBuilder sb, XElement el, Context ctx)
    {
        switch (el.Name.LocalName)
        {
            case "see":
            case "seealso":
            {
                var langword = el.Attribute("langword")?.Value;
                var cref = el.Attribute("cref")?.Value;
                var href = el.Attribute("href")?.Value;
                if (!el.IsEmpty && el.Nodes().Any())
                    AppendNodes(sb, el.Nodes(), ctx); // explicit link text wins
                else if (langword != null)
                    sb.Append(langword);
                else if (cref != null)
                    sb.Append(FormatCref(cref, ctx));
                else if (href != null)
                    sb.Append(href);
                break;
            }
            case "paramref":
            case "typeparamref":
                sb.Append(el.Attribute("name")?.Value ?? "");
                break;
            case "code":
                sb.Append(' ').Append(el.Value).Append(' ');
                break;
            case "para":
            case "br":
            case "list":
            case "item":
            case "listheader":
            case "description":
            case "term":
                // Block-level elements: keep words from running together
                sb.Append(' ');
                AppendNodes(sb, el.Nodes(), ctx);
                sb.Append(' ');
                break;
            default:
                // <c>, <b>, <i>, <a>, ... — keep their (recursively resolved) content
                AppendNodes(sb, el.Nodes(), ctx);
                break;
        }
    }

    /// <summary>
    /// Formats a documentation-comment cref (e.g. <c>M:System.String.Join(System.String,System.String[])</c>)
    /// for display: types as <c>List&lt;T&gt;</c>, members as <c>Type.Member</c>.
    /// </summary>
    internal static string FormatCref(string cref, Compilation? compilation) => FormatCref(cref, new Context(compilation));

    private static string FormatCref(string cref, Context ctx)
    {
        if (string.IsNullOrWhiteSpace(cref)) return "";

        if (ctx.Compilation != null && cref.Length > 2 && cref[1] == ':' && cref[0] != '!')
        {
            try
            {
                var symbol = DocumentationCommentId.GetFirstSymbolForDeclarationId(cref, ctx.Compilation);
                if (symbol != null)
                    return FormatSymbol(symbol);
            }
            catch (ArgumentException)
            {
                // fall through to text-based formatting
            }
        }

        return FormatCrefText(cref);
    }

    private static string FormatSymbol(ISymbol symbol) => symbol switch
    {
        ITypeSymbol type => type.ToDisplayString(CrefTypeFormat),
        INamespaceSymbol ns => ns.Name,
        _ when symbol.ContainingType != null =>
            $"{symbol.ContainingType.ToDisplayString(CrefTypeFormat)}.{MemberName(symbol)}",
        _ => symbol.Name,
    };

    private static string MemberName(ISymbol symbol) => symbol switch
    {
        IMethodSymbol { MethodKind: MethodKind.Constructor } m => m.ContainingType.Name,
        _ => symbol.Name,
    };

    /// <summary>Best-effort formatting when the cref cannot be resolved to a symbol.</summary>
    internal static string FormatCrefText(string cref)
    {
        var kind = 'T';
        if (cref.Length > 2 && cref[1] == ':')
        {
            kind = cref[0];
            cref = cref[2..];
        }

        // Drop the parameter list before splitting on '.' (it contains dots of its own)
        var paren = cref.IndexOf('(');
        if (paren >= 0) cref = cref[..paren];

        cref = ArityPattern().Replace(cref, "").Replace('{', '<').Replace('}', '>');

        var segments = SplitOutsideBrackets(cref);
        if (segments.Count == 0) return cref;

        var isMember = kind is 'M' or 'P' or 'F' or 'E';
        var take = isMember && segments.Count >= 2 ? 2 : 1;
        var result = string.Join(".", segments.Skip(segments.Count - take));
        return result.Replace("#ctor", segments.Count >= 2 ? segments[^2] : "ctor");
    }

    private static List<string> SplitOutsideBrackets(string value)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;
        for (var i = 0; i < value.Length; i++)
        {
            switch (value[i])
            {
                case '<': depth++; break;
                case '>': depth--; break;
                case '.' when depth == 0:
                    parts.Add(value[start..i]);
                    start = i + 1;
                    break;
            }
        }
        parts.Add(value[start..]);
        return parts.Where(p => p.Length > 0).ToList();
    }

    private sealed record Context(Compilation? Compilation);
}
