using System.Net;
using System.Text;

namespace TwoHash.Core;

public class HtmlRenderOptions
{
    public bool Standalone { get; init; }
}

public class HtmlRenderer
{
    /// <summary>
    /// Renders a TwohashResult as self-contained HTML.
    /// The tokens list should be classified spans for result.Code (the processed source).
    /// </summary>
    public static string Render(
        TwohashResult result,
        List<ClassifiedToken> tokens,
        TwohashTheme theme,
        HtmlRenderOptions? options = null)
    {
        options ??= new HtmlRenderOptions();
        var sb = new StringBuilder();

        if (options.Standalone)
        {
            sb.AppendLine("<!DOCTYPE html>");
            sb.AppendLine("<html lang=\"en\">");
            sb.AppendLine("<head>");
            sb.AppendLine("<meta charset=\"utf-8\">");
            sb.AppendLine("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">");
            sb.AppendLine("<title>twohash</title>");
            sb.AppendLine("</head>");
            sb.AppendLine("<body>");
        }

        sb.AppendLine($"<div class=\"twohash-code\" data-theme=\"{Encode(theme.Name)}\">");
        sb.AppendLine("<style>");
        sb.Append(GenerateStyles(theme));
        sb.AppendLine("</style>");

        var code = result.Code;
        var lines = code.Split('\n');

        // Compute line start offsets in the source string
        var lineStarts = new int[lines.Length];
        var offset = 0;
        for (var i = 0; i < lines.Length; i++)
        {
            lineStarts[i] = offset;
            offset += lines[i].Length + 1; // +1 for \n
        }

        // Build line classes from highlights
        var lineClasses = ComputeLineClasses(lines.Length, result.Highlights);

        // Index tokens by their position for quick lookup
        var tokenIndex = 0;

        sb.Append($"<pre style=\"background:{theme.Background};color:{theme.Foreground};padding:16px;border-radius:6px;overflow-x:auto;margin:0;font-family:'Cascadia Code','Fira Code',Consolas,monospace;\"><code>");

        for (var lineIdx = 0; lineIdx < lines.Length; lineIdx++)
        {
            var lineClass = lineClasses[lineIdx];
            sb.Append(lineClass != null
                ? $"<span class=\"line {lineClass}\">"
                : "<span class=\"line\">");

            var lineStart = lineStarts[lineIdx];
            var lineEnd = lineStart + lines[lineIdx].Length;

            // Render tokens that fall within this line
            RenderLineFromTokens(sb, code, lineStart, lineEnd, lineIdx, ref tokenIndex,
                tokens, theme, result.Hovers, result.Errors);

            sb.Append("</span>");
            if (lineIdx < lines.Length - 1)
                sb.Append('\n');

            // Render completion list after this line
            var completion = result.Completions.FirstOrDefault(c => c.Line == lineIdx);
            if (completion != null)
            {
                sb.Append('\n');
                RenderCompletionList(sb, completion, theme);
            }
        }

        sb.AppendLine("</code></pre>");

        // Render popup elements (positioned via CSS anchors)
        for (var i = 0; i < result.Hovers.Count; i++)
        {
            RenderPopup(sb, result.Hovers[i], i, theme);
        }

        // Render error messages
        foreach (var error in result.Errors)
        {
            RenderErrorMessage(sb, error);
        }

        sb.AppendLine("</div>");

        if (options.Standalone)
        {
            sb.AppendLine("</body>");
            sb.AppendLine("</html>");
        }

        return sb.ToString();
    }

    private static void RenderLineFromTokens(
        StringBuilder sb,
        string fullSource,
        int lineStart,
        int lineEnd,
        int lineIdx,
        ref int tokenIndex,
        List<ClassifiedToken> tokens,
        TwohashTheme theme,
        List<TwohashHover> hovers,
        List<TwohashError> errors)
    {
        // Build hover/error lookups for this line
        var lineHovers = new Dictionary<int, int>(); // character -> hover index
        for (var i = 0; i < hovers.Count; i++)
        {
            if (hovers[i].Line == lineIdx)
                lineHovers[hovers[i].Character] = i;
        }

        var lineErrors = new Dictionary<int, TwohashError>();
        foreach (var error in errors)
        {
            if (error.Line == lineIdx && !lineErrors.ContainsKey(error.Character))
                lineErrors[error.Character] = error;
        }

        var pos = lineStart;

        // Advance tokenIndex past tokens before this line
        while (tokenIndex < tokens.Count && tokens[tokenIndex].Start + tokens[tokenIndex].Length <= lineStart)
            tokenIndex++;

        var savedTokenIndex = tokenIndex;

        while (savedTokenIndex < tokens.Count)
        {
            var token = tokens[savedTokenIndex];
            if (token.Start >= lineEnd) break;

            // Clamp token to line bounds
            var tokenStart = Math.Max(token.Start, lineStart);
            var tokenEnd = Math.Min(token.Start + token.Length, lineEnd);
            if (tokenStart >= tokenEnd)
            {
                savedTokenIndex++;
                continue;
            }

            // Fill gap before token
            if (tokenStart > pos)
            {
                sb.Append(Encode(fullSource[pos..tokenStart]));
            }

            var charInLine = tokenStart - lineStart;
            var tokenText = fullSource[tokenStart..tokenEnd];
            var kind = token.Kind;

            var isHover = lineHovers.TryGetValue(charInLine, out var hoverIdx);
            var isError = lineErrors.TryGetValue(charInLine, out var error);

            var color = theme.GetTokenColor(kind);
            var colorAttr = color != theme.Foreground ? $" style=\"color:{color}\"" : "";

            if (isHover)
            {
                sb.Append($"<span class=\"twohash-hover\" style=\"anchor-name:--th-{hoverIdx}\">");
                sb.Append($"<span{colorAttr}>{Encode(tokenText)}</span>");
                sb.Append("</span>");
            }
            else if (isError)
            {
                sb.Append("<span class=\"twohash-error-underline\">");
                sb.Append($"<span{colorAttr}>{Encode(tokenText)}</span>");
                sb.Append("</span>");
            }
            else if (kind == "whitespace" || (kind == "text" && tokenText.Trim().Length == 0))
            {
                sb.Append(Encode(tokenText));
            }
            else
            {
                sb.Append($"<span{colorAttr}>{Encode(tokenText)}</span>");
            }

            pos = tokenEnd;
            savedTokenIndex++;
        }

        // Trailing content on this line
        if (pos < lineEnd)
        {
            sb.Append(Encode(fullSource[pos..lineEnd]));
        }
    }

    private static void RenderPopup(StringBuilder sb, TwohashHover hover, int index, TwohashTheme theme)
    {
        sb.Append($"<div class=\"twohash-popup\" style=\"position-anchor:--th-{index}\">");
        sb.Append("<code class=\"twohash-popup-code\">");

        foreach (var part in hover.Parts)
        {
            var color = theme.GetTokenColor(PartKindToClassificationKind(part.Kind));
            sb.Append($"<span style=\"color:{color}\">{Encode(part.Text)}</span>");
        }

        sb.Append("</code>");

        if (hover.Docs != null)
        {
            sb.Append("<div class=\"twohash-popup-docs\">");
            if (hover.Docs.Summary != null)
            {
                sb.Append($"<div class=\"twohash-popup-summary\">{Encode(hover.Docs.Summary)}</div>");
            }
            if (hover.Docs.Params.Count > 0)
            {
                sb.Append("<div class=\"twohash-popup-params\">");
                sb.Append("<div class=\"twohash-popup-section-label\">Parameters</div>");
                foreach (var p in hover.Docs.Params)
                {
                    sb.Append($"<div class=\"twohash-popup-param\"><span class=\"twohash-popup-param-name\">{Encode(p.Name)}</span> \u2014 {Encode(p.Text)}</div>");
                }
                sb.Append("</div>");
            }
            if (hover.Docs.Returns != null)
            {
                sb.Append("<div class=\"twohash-popup-returns\">");
                sb.Append("<div class=\"twohash-popup-section-label\">Returns</div>");
                sb.Append(Encode(hover.Docs.Returns));
                sb.Append("</div>");
            }
            sb.Append("</div>");
        }

        sb.AppendLine("</div>");
    }

    private static void RenderErrorMessage(StringBuilder sb, TwohashError error)
    {
        sb.Append("<div class=\"twohash-error-message\">");
        sb.Append($"<span class=\"twohash-error-code\">{Encode(error.Code)}</span>");
        sb.Append($": {Encode(error.Message)}");
        sb.AppendLine("</div>");
    }

    private static void RenderCompletionList(StringBuilder sb, TwohashCompletion completion, TwohashTheme theme)
    {
        sb.Append("<ul class=\"twohash-completion-list\">");
        foreach (var item in completion.Items)
        {
            sb.Append("<li class=\"twohash-completion-item\">");
            sb.Append($"<span class=\"twohash-completion-kind\">{Encode(item.Kind)}</span>");
            sb.Append($"<span class=\"twohash-completion-label\">{Encode(item.Label)}</span>");
            if (item.Detail != null)
                sb.Append($"<span class=\"twohash-completion-detail\">{Encode(item.Detail)}</span>");
            sb.Append("</li>");
        }
        sb.AppendLine("</ul>");
    }

    private static string?[] ComputeLineClasses(int lineCount, List<TwohashHighlight> highlights)
    {
        var classes = new string?[lineCount];
        var hasFocus = highlights.Any(h => h.Kind == "focus");
        var focusedLines = new HashSet<int>(highlights.Where(h => h.Kind == "focus").Select(h => h.Line));

        foreach (var h in highlights)
        {
            if (h.Line < 0 || h.Line >= lineCount) continue;
            classes[h.Line] = h.Kind switch
            {
                "highlight" => "twohash-highlight",
                "add" => "twohash-diff-add",
                "remove" => "twohash-diff-remove",
                _ => classes[h.Line],
            };
        }

        if (hasFocus)
        {
            for (var i = 0; i < lineCount; i++)
            {
                if (!focusedLines.Contains(i) && classes[i] == null)
                    classes[i] = "twohash-focus-dim";
            }
        }

        return classes;
    }

    private static string PartKindToClassificationKind(string partKind) => partKind switch
    {
        "keyword" => "keyword",
        "className" or "structName" or "interfaceName" or "enumName" or "delegateName" => partKind,
        "typeParameterName" => "typeParameterName",
        "methodName" => "methodName",
        "propertyName" => "propertyName",
        "fieldName" => "fieldName",
        "eventName" => "eventName",
        "localName" => "localName",
        "parameterName" => "parameterName",
        "namespaceName" => "namespaceName",
        "punctuation" => "punctuation",
        "operator" => "operator",
        _ => "text",
    };

    private static string GenerateStyles(TwohashTheme theme) => $@"
.twohash-code .line {{
  display: block;
}}
.twohash-hover {{
  border-bottom: 1px dotted currentColor;
  cursor: pointer;
}}
.twohash-popup {{
  display: none;
  position: fixed;
  position-area: top;
  margin-bottom: 4px;
  z-index: 100;
  max-width: 500px;
  padding: 8px 12px;
  border: 1px solid {theme.PopupBorder};
  border-radius: 4px;
  background: {theme.PopupBackground};
  color: {theme.PopupForeground};
  font-size: 0.875em;
  line-height: 1.5;
  white-space: pre-wrap;
  box-shadow: 0 2px 8px rgba(0,0,0,0.4);
}}
.twohash-hover:hover + .twohash-popup,
.twohash-popup:hover {{
  display: block;
}}
.twohash-popup-code {{
  font-family: inherit;
}}
.twohash-popup-docs {{
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px solid {theme.PopupBorder};
}}
.twohash-popup-summary {{
  font-style: italic;
}}
.twohash-popup-params,
.twohash-popup-returns {{
  margin-top: 4px;
  padding-top: 4px;
  border-top: 1px solid {theme.PopupBorder};
}}
.twohash-popup-section-label {{
  font-size: 0.8em;
  opacity: 0.7;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 2px;
}}
.twohash-popup-param {{
  display: flex;
  gap: 6px;
  margin: 1px 0;
}}
.twohash-popup-param-name {{
  font-weight: bold;
  white-space: nowrap;
}}
.twohash-error-underline {{
  border-bottom: 2px wavy {theme.ErrorColor};
}}
.twohash-error-message {{
  display: block;
  padding: 2px 8px;
  margin-top: 2px;
  background: {theme.ErrorBackground};
  border-left: 3px solid {theme.ErrorColor};
  color: {theme.ErrorColor};
  font-size: 0.85em;
}}
.twohash-error-code {{
  font-weight: bold;
}}
.twohash-completion-list {{
  list-style: none;
  margin: 4px 0 0 0;
  padding: 4px 0;
  border: 1px solid {theme.PopupBorder};
  border-radius: 4px;
  background: {theme.PopupBackground};
  font-size: 0.875em;
  max-height: 200px;
  overflow-y: auto;
}}
.twohash-completion-item {{
  display: flex;
  gap: 8px;
  padding: 2px 8px;
  align-items: center;
}}
.twohash-completion-kind {{
  font-size: 0.75em;
  opacity: 0.7;
  min-width: 60px;
}}
.twohash-completion-label {{
  color: {theme.PopupForeground};
}}
.twohash-completion-detail {{
  opacity: 0.6;
  font-size: 0.85em;
  margin-left: auto;
}}
.twohash-highlight {{
  background: {theme.HighlightBackground};
}}
.twohash-focus-dim {{
  opacity: {theme.FocusDimOpacity};
  transition: opacity 0.2s;
}}
.twohash-diff-add {{
  background: {theme.DiffAddBackground};
  border-left: 3px solid {theme.DiffAddBorder};
}}
.twohash-diff-remove {{
  background: {theme.DiffRemoveBackground};
  border-left: 3px solid {theme.DiffRemoveBorder};
}}
@supports not (anchor-name: --x) {{
  .twohash-hover {{
    position: relative;
  }}
  .twohash-popup {{
    position: absolute;
    bottom: 100%;
    left: 0;
  }}
}}
";

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
