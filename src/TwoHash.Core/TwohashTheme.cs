namespace TwoHash.Core;

public record TwohashTheme
{
    public required string Name { get; init; }

    // Base colors
    public required string Background { get; init; }
    public required string Foreground { get; init; }

    // Token colors (keyed by classification kind)
    public required Dictionary<string, string> TokenColors { get; init; }

    // Popup colors
    public required string PopupBackground { get; init; }
    public required string PopupForeground { get; init; }
    public required string PopupBorder { get; init; }

    // Error colors
    public required string ErrorColor { get; init; }
    public required string ErrorBackground { get; init; }

    // Highlight colors
    public required string HighlightBackground { get; init; }
    public required string FocusDimOpacity { get; init; }

    // Diff colors
    public required string DiffAddBackground { get; init; }
    public required string DiffAddBorder { get; init; }
    public required string DiffRemoveBackground { get; init; }
    public required string DiffRemoveBorder { get; init; }

    public string GetTokenColor(string kind) =>
        TokenColors.GetValueOrDefault(kind, Foreground);

    public static TwohashTheme? GetBuiltIn(string name) => name switch
    {
        "github-dark" => GithubDark,
        "github-light" => GithubLight,
        _ => null,
    };

    public static readonly string[] BuiltInNames = ["github-dark", "github-light"];

    public static readonly TwohashTheme GithubDark = new()
    {
        Name = "github-dark",
        Background = "#0d1117",
        Foreground = "#e6edf3",
        TokenColors = new Dictionary<string, string>
        {
            ["keyword"] = "#ff7b72",
            ["className"] = "#f0883e",
            ["structName"] = "#f0883e",
            ["interfaceName"] = "#f0883e",
            ["enumName"] = "#f0883e",
            ["delegateName"] = "#f0883e",
            ["typeParameterName"] = "#f0883e",
            ["methodName"] = "#d2a8ff",
            ["propertyName"] = "#79c0ff",
            ["fieldName"] = "#79c0ff",
            ["eventName"] = "#79c0ff",
            ["localName"] = "#e6edf3",
            ["parameterName"] = "#e6edf3",
            ["namespaceName"] = "#e6edf3",
            ["string"] = "#a5d6ff",
            ["number"] = "#79c0ff",
            ["comment"] = "#8b949e",
            ["operator"] = "#ff7b72",
            ["punctuation"] = "#e6edf3",
            ["text"] = "#e6edf3",
        },
        PopupBackground = "#161b22",
        PopupForeground = "#e6edf3",
        PopupBorder = "#30363d",
        ErrorColor = "#f85149",
        ErrorBackground = "rgba(248, 81, 73, 0.1)",
        HighlightBackground = "rgba(173, 124, 255, 0.15)",
        FocusDimOpacity = "0.4",
        DiffAddBackground = "rgba(46, 160, 67, 0.15)",
        DiffAddBorder = "#2ea043",
        DiffRemoveBackground = "rgba(248, 81, 73, 0.15)",
        DiffRemoveBorder = "#f85149",
    };

    public static readonly TwohashTheme GithubLight = new()
    {
        Name = "github-light",
        Background = "#ffffff",
        Foreground = "#1f2328",
        TokenColors = new Dictionary<string, string>
        {
            ["keyword"] = "#cf222e",
            ["className"] = "#953800",
            ["structName"] = "#953800",
            ["interfaceName"] = "#953800",
            ["enumName"] = "#953800",
            ["delegateName"] = "#953800",
            ["typeParameterName"] = "#953800",
            ["methodName"] = "#8250df",
            ["propertyName"] = "#0550ae",
            ["fieldName"] = "#0550ae",
            ["eventName"] = "#0550ae",
            ["localName"] = "#1f2328",
            ["parameterName"] = "#1f2328",
            ["namespaceName"] = "#1f2328",
            ["string"] = "#0a3069",
            ["number"] = "#0550ae",
            ["comment"] = "#6e7781",
            ["operator"] = "#cf222e",
            ["punctuation"] = "#1f2328",
            ["text"] = "#1f2328",
        },
        PopupBackground = "#f6f8fa",
        PopupForeground = "#1f2328",
        PopupBorder = "#d0d7de",
        ErrorColor = "#cf222e",
        ErrorBackground = "rgba(207, 34, 46, 0.1)",
        HighlightBackground = "rgba(139, 90, 230, 0.12)",
        FocusDimOpacity = "0.4",
        DiffAddBackground = "rgba(46, 160, 67, 0.12)",
        DiffAddBorder = "#2ea043",
        DiffRemoveBackground = "rgba(248, 81, 73, 0.12)",
        DiffRemoveBorder = "#f85149",
    };
}
