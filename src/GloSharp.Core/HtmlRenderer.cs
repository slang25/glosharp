using System.Globalization;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace GloSharp.Core;

public class HtmlRenderOptions
{
    /// <summary>Wrap the fragment in a complete HTML document.</summary>
    public bool Standalone { get; init; }

    /// <summary>
    /// Embed the stylesheet. Turn this off when the page already includes
    /// <see cref="HtmlRenderer.GenerateStylesheet"/> once for the theme, so twenty
    /// snippets don't carry twenty copies of it.
    /// </summary>
    public bool IncludeStyles { get; init; } = true;

    /// <summary>Document title for <see cref="Standalone"/> output.</summary>
    public string? Title { get; init; }
}

public class HtmlRenderer
{
    /// <summary>
    /// Renders a GloSharpResult as self-contained, script-free HTML.
    /// The tokens list should be classified spans for result.Code (the processed source).
    /// </summary>
    public static string Render(
        GloSharpResult result,
        List<ClassifiedToken> tokens,
        GloSharpTheme theme,
        HtmlRenderOptions? options = null)
    {
        options ??= new HtmlRenderOptions();
        var sb = new StringBuilder();

        if (options.Standalone)
        {
            sb.Append("<!DOCTYPE html>\n");
            sb.Append("<html lang=\"en\">\n");
            sb.Append("<head>\n");
            sb.Append("<meta charset=\"utf-8\">\n");
            sb.Append("<meta name=\"viewport\" content=\"width=device-width, initial-scale=1\">\n");
            sb.Append($"<title>{Encode(options.Title ?? "glosharp")}</title>\n");
            // A <style> is only valid in <head>, so the standalone page keeps it there.
            sb.Append("<style>\n").Append(PageStyles(theme));
            if (options.IncludeStyles)
                sb.Append(GenerateStylesheet(theme));
            sb.Append("</style>\n");
            sb.Append("</head>\n");
            sb.Append("<body>\n");
        }

        sb.Append($"<div class=\"glosharp-code\" data-theme=\"{Encode(theme.Name)}\">\n");
        if (!options.Standalone && options.IncludeStyles)
        {
            // A fragment has nowhere else to carry its styles. Every selector is
            // scoped to this theme's root, so it cannot restyle anything else.
            sb.Append("<style>\n").Append(GenerateStylesheet(theme)).Append("</style>\n");
        }

        new FragmentWriter(sb, result, tokens, theme).Write();

        sb.Append("</div>\n");

        if (options.Standalone)
        {
            sb.Append("</body>\n");
            sb.Append("</html>\n");
        }

        return sb.ToString();
    }

    /// <summary>
    /// The CSS for one theme. Every selector is scoped to
    /// <c>.glosharp-code[data-theme="…"]</c>, so fragments in different themes and
    /// output from the other renderers can share a page.
    /// </summary>
    public static string GenerateStylesheet(GloSharpTheme theme) => BuildStylesheet(theme);

    /// <summary>
    /// Writes the code block for one fragment. Each source line is split into runs
    /// at every token, hover and diagnostic boundary, so the three layers can
    /// overlap freely: a hover wraps its own range, an underline wraps exactly the
    /// diagnostic's range inside it, and colour spans sit innermost.
    /// </summary>
    private sealed class FragmentWriter
    {
        private readonly StringBuilder _sb;
        private readonly GloSharpResult _result;
        private readonly List<ClassifiedToken> _tokens;
        private readonly GloSharpTheme _theme;
        private readonly string _scope;
        private readonly string _code;
        private readonly string[] _lines;
        private readonly int[] _lineStarts;

        private readonly Dictionary<int, List<int>> _hoversByLine = [];
        private readonly Dictionary<int, List<(int Start, int End, int Rank)>> _underlinesByLine = [];
        private readonly Dictionary<int, List<GloSharpError>> _messagesByLine = [];
        private readonly Dictionary<int, List<GloSharpTag>> _tagsByLine = [];
        private readonly Dictionary<int, List<GloSharpCompletion>> _completionsByLine = [];
        private int _tokenCursor;

        public FragmentWriter(StringBuilder sb, GloSharpResult result, List<ClassifiedToken> tokens, GloSharpTheme theme)
        {
            _sb = sb;
            _result = result;
            _tokens = tokens;
            _theme = theme;
            _code = result.Code;
            _lines = _code.Split('\n');
            _lineStarts = new int[_lines.Length];
            var offset = 0;
            for (var i = 0; i < _lines.Length; i++)
            {
                _lineStarts[i] = offset;
                offset += _lines[i].Length + 1;
            }

            // Anchor names and ids live in a document-wide namespace. Derive the
            // prefix from the code and theme so two fragments on one page never
            // share one, while the bytes stay deterministic across runs.
            _scope = ScopeId(_code, theme.Name);

            for (var i = 0; i < result.Hovers.Count; i++)
                Bucket(_hoversByLine, result.Hovers[i].Line, i);

            var lastLine = _lines.Length - 1;
            foreach (var error in result.Errors)
            {
                if (error.Severity == "hidden") continue;
                AddUnderlines(error);
                var messageLine = Math.Clamp(error.EndLine ?? error.Line, 0, lastLine);
                Bucket(_messagesByLine, messageLine, error);
            }
            foreach (var tag in result.Tags)
                Bucket(_tagsByLine, Math.Clamp(tag.Line, 0, lastLine), tag);
            foreach (var completion in result.Completions)
                Bucket(_completionsByLine, Math.Clamp(completion.Line, 0, lastLine), completion);
        }

        public void Write()
        {
            var lineClasses = ComputeLineClasses(_lines.Length, _result.Highlights);

            _sb.Append("<pre><code>");
            for (var lineIdx = 0; lineIdx < _lines.Length; lineIdx++)
            {
                var isLast = lineIdx == _lines.Length - 1;
                // The source's final newline leaves an empty last line; it renders
                // nothing, so it gets no element.
                if (!(isLast && _lines[lineIdx].Length == 0 && lineIdx > 0))
                {
                    var lineClass = lineClasses[lineIdx];
                    _sb.Append(lineClass != null ? $"<span class=\"line {lineClass}\">" : "<span class=\"line\">");
                    WriteLine(lineIdx);
                    _sb.Append("</span>");
                }

                // These newlines are the only thing that breaks a line: `.line` is
                // inline, because a block boundary counts as a second break in
                // Chromium and as none at all in Firefox's clipboard.
                if (!isLast)
                    _sb.Append('\n');

                // Callouts are blocks after the newline, so they occupy rows of
                // their own without adding a line break to the code.
                WriteCallouts(lineIdx);
            }
            _sb.Append("</code></pre>\n");
        }

        private void WriteLine(int lineIdx)
        {
            var line = _lines[lineIdx];
            var lineStart = _lineStarts[lineIdx];
            var len = line.Length;

            var tok = new int[len];
            Array.Fill(tok, -1);
            while (_tokenCursor < _tokens.Count && _tokens[_tokenCursor].Start + _tokens[_tokenCursor].Length <= lineStart)
                _tokenCursor++;
            for (var k = _tokenCursor; k < _tokens.Count && _tokens[k].Start < lineStart + len; k++)
            {
                var from = Math.Max(_tokens[k].Start, lineStart) - lineStart;
                var to = Math.Min(_tokens[k].Start + _tokens[k].Length, lineStart + len) - lineStart;
                for (var c = from; c < to; c++) tok[c] = k;
            }

            var hov = new int[len];
            Array.Fill(hov, -1);
            if (_hoversByLine.TryGetValue(lineIdx, out var hoverIndexes))
            {
                foreach (var h in hoverIndexes)
                {
                    var hover = _result.Hovers[h];
                    var from = Math.Max(hover.Character, 0);
                    var to = Math.Min(hover.Character + Math.Max(hover.Length, 1), len);
                    if (from >= to) continue;
                    // One anchor per hover: a hover that collides with an earlier
                    // one is skipped rather than split into two anchors.
                    var free = true;
                    for (var c = from; c < to && free; c++) free = hov[c] < 0;
                    if (!free) continue;
                    for (var c = from; c < to; c++) hov[c] = h;
                }
            }

            var err = new int[len];
            var eolRank = 0;
            if (_underlinesByLine.TryGetValue(lineIdx, out var underlines))
            {
                foreach (var (start, end, rank) in underlines)
                {
                    var (from, to) = ResolveUnderlineRange(line, lineStart, tok, start, end);
                    if (from >= to)
                    {
                        eolRank = Math.Max(eolRank, rank);
                        continue;
                    }
                    for (var c = from; c < to; c++) err[c] = Math.Max(err[c], rank);
                }
            }

            var pos = 0;
            while (pos < len)
            {
                var h = hov[pos];
                var end = pos;
                while (end < len && hov[end] == h) end++;
                if (h >= 0)
                {
                    var hover = _result.Hovers[h];
                    var id = $"{_scope}-{h}";
                    var cls = hover.Persistent ? "glosharp-hover glosharp-hover-persistent" : "glosharp-hover";
                    // The popup is nested inside its hover span: that is what lets
                    // :hover and :focus-visible on the token reveal it.
                    _sb.Append($"<span class=\"{cls}\" tabindex=\"0\" aria-describedby=\"{id}\" style=\"anchor-name:--{id}\">");
                    WriteUnderlineRuns(line, tok, err, pos, end, wrapEveryRun: true);
                    _sb.Append($"<span class=\"glosharp-popup\" role=\"tooltip\" id=\"{id}\" style=\"position-anchor:--{id}\">");
                    WritePopupContent(hover, full: true);
                    _sb.Append("</span></span>");
                }
                else
                {
                    WriteUnderlineRuns(line, tok, err, pos, end, wrapEveryRun: false);
                }
                pos = end;
            }

            if (eolRank > 0)
            {
                // A diagnostic on a blank line has no text to underline; this
                // marker draws a short squiggle from generated content, which is
                // never copied with the code.
                _sb.Append($"<span class=\"glosharp-error-underline glosharp-error-marker {SeverityClass(eolRank)}\"></span>");
            }
        }

        /// <summary>
        /// Zero-width diagnostics (CS1002 "; expected" and friends) sit between
        /// characters, often past the end of the line. Underline the token they
        /// follow — or, at the start of a line, the token they precede — the way
        /// an editor does.
        /// </summary>
        private (int From, int To) ResolveUnderlineRange(string line, int lineStart, int[] tok, int start, int end)
        {
            var len = line.Length;
            start = Math.Clamp(start, 0, len);
            end = Math.Clamp(end, start, len);
            if (end > start && line[start..end].Trim().Length > 0)
                return (start, end);

            var p = start - 1;
            while (p >= 0 && char.IsWhiteSpace(line[p])) p--;
            if (p < 0)
            {
                p = end;
                while (p < len && char.IsWhiteSpace(line[p])) p++;
                if (p >= len) return (0, 0);
            }
            return TokenExtent(line, lineStart, tok, p);
        }

        private (int From, int To) TokenExtent(string line, int lineStart, int[] tok, int c)
        {
            if (tok[c] >= 0)
            {
                var t = _tokens[tok[c]];
                return (Math.Max(t.Start - lineStart, 0), Math.Min(t.Start + t.Length - lineStart, line.Length));
            }
            var from = c;
            var to = c + 1;
            while (from > 0 && tok[from - 1] < 0 && !char.IsWhiteSpace(line[from - 1])) from--;
            while (to < line.Length && tok[to] < 0 && !char.IsWhiteSpace(line[to])) to++;
            return (from, to);
        }

        private void WriteUnderlineRuns(string line, int[] tok, int[] err, int from, int to, bool wrapEveryRun)
        {
            var p = from;
            while (p < to)
            {
                var rank = err[p];
                var q = p;
                while (q < to && err[q] == rank) q++;
                if (rank > 0)
                {
                    _sb.Append($"<span class=\"glosharp-error-underline {SeverityClass(rank)}\">");
                    WriteColorRuns(line, tok, p, q, wrapEveryRun);
                    _sb.Append("</span>");
                }
                else
                {
                    WriteColorRuns(line, tok, p, q, wrapEveryRun);
                }
                p = q;
            }
        }

        private void WriteColorRuns(string line, int[] tok, int from, int to, bool wrapEveryRun)
        {
            var p = from;
            while (p < to)
            {
                var color = ColorAt(tok, p);
                var q = p + 1;
                while (q < to && ColorAt(tok, q) == color) q++;
                var text = Encode(line[p..q]);
                if (color != null)
                    _sb.Append($"<span style=\"color:{color}\">{text}</span>");
                else if (wrapEveryRun)
                    _sb.Append($"<span>{text}</span>");
                else
                    _sb.Append(text);
                p = q;
            }
        }

        /// <summary>Token colour at a character, or null for the default foreground.</summary>
        private string? ColorAt(int[] tok, int c)
        {
            if (tok[c] < 0) return null;
            var kind = _tokens[tok[c]].Kind;
            if (kind == "whitespace") return null;
            var color = _theme.GetTokenColor(kind);
            return string.Equals(color, _theme.Foreground, StringComparison.OrdinalIgnoreCase) ? null : color;
        }

        private void WriteCallouts(int lineIdx)
        {
            if (_hoversByLine.TryGetValue(lineIdx, out var hoverIndexes))
            {
                foreach (var h in hoverIndexes.Where(h => _result.Hovers[h].Persistent).OrderBy(h => _result.Hovers[h].Character))
                    WriteStaticQuery(_result.Hovers[h]);
            }

            if (_messagesByLine.TryGetValue(lineIdx, out var errors))
            {
                foreach (var error in errors)
                    WriteErrorMessage(error);
            }

            if (_tagsByLine.TryGetValue(lineIdx, out var tags))
            {
                foreach (var tag in tags)
                    WriteTag(tag);
            }

            if (_completionsByLine.TryGetValue(lineIdx, out var completions))
            {
                foreach (var completion in completions)
                    WriteCompletionList(completion);
            }
        }

        /// <summary>A persistent <c>^?</c> query: always visible, under the token it asks about.</summary>
        private void WriteStaticQuery(GloSharpHover hover)
        {
            _sb.Append($"<span class=\"glosharp-callout glosharp-static\" style=\"--glosharp-column:{Math.Max(hover.Character, 0)}ch\">");
            _sb.Append("<span class=\"glosharp-static-container\">");
            WritePopupContent(hover, full: false);
            _sb.Append("</span></span>");
        }

        private void WritePopupContent(GloSharpHover hover, bool full)
        {
            _sb.Append("<code class=\"glosharp-popup-code\">");
            foreach (var part in hover.Parts)
            {
                var kind = PartKindToClassificationKind(part.Kind);
                if (kind == "text")
                {
                    _sb.Append(Encode(part.Text));
                    continue;
                }
                _sb.Append($"<span style=\"color:{_theme.GetTokenColor(kind)}\">{Encode(part.Text)}</span>");
            }
            if (hover.OverloadCount is > 1 and var count)
            {
                var others = count - 1;
                _sb.Append($" <span class=\"glosharp-popup-overloads\">(+{others} overload{(others == 1 ? "" : "s")})</span>");
            }
            _sb.Append("</code>");

            if (full && hover.TypeAnnotations is { Count: > 0 } annotations)
            {
                _sb.Append("<span class=\"glosharp-popup-types\">");
                _sb.Append("<span class=\"glosharp-popup-section-label\">Types</span>");
                foreach (var a in annotations)
                {
                    _sb.Append($"<span class=\"glosharp-type-annotation\"><span style=\"color:{_theme.GetTokenColor("className")}\">{Encode(a.Name)}</span> is <span class=\"glosharp-type-expansion\">{Encode(a.Expansion)}</span></span>");
                }
                _sb.Append("</span>");
            }

            if (hover.Docs is { } docs)
                WriteDocs(docs, full);
        }

        private void WriteDocs(GloSharpDocComment docs, bool full)
        {
            var sections = new StringBuilder();
            if (!string.IsNullOrWhiteSpace(docs.Summary))
                sections.Append($"<span class=\"glosharp-popup-summary\">{Encode(docs.Summary)}</span>");

            if (full)
            {
                if (docs.Params.Count > 0)
                {
                    sections.Append("<span class=\"glosharp-popup-params\"><span class=\"glosharp-popup-section-label\">Parameters</span>");
                    foreach (var p in docs.Params)
                        sections.Append($"<span class=\"glosharp-popup-param\"><span class=\"glosharp-popup-param-name\">{Encode(p.Name)}</span> — {Encode(p.Text)}</span>");
                    sections.Append("</span>");
                }
                if (!string.IsNullOrWhiteSpace(docs.Returns))
                    sections.Append($"<span class=\"glosharp-popup-returns\"><span class=\"glosharp-popup-section-label\">Returns</span>{Encode(docs.Returns)}</span>");
                if (!string.IsNullOrWhiteSpace(docs.Remarks))
                    sections.Append($"<span class=\"glosharp-popup-remarks\"><span class=\"glosharp-popup-section-label\">Remarks</span>{Encode(docs.Remarks)}</span>");
                if (docs.Examples.Count > 0)
                {
                    sections.Append("<span class=\"glosharp-popup-example\"><span class=\"glosharp-popup-section-label\">Examples</span>");
                    foreach (var example in docs.Examples)
                        sections.Append($"<span class=\"glosharp-popup-example-code\">{Encode(example)}</span>");
                    sections.Append("</span>");
                }
                if (docs.Exceptions.Count > 0)
                {
                    sections.Append("<span class=\"glosharp-popup-exceptions\"><span class=\"glosharp-popup-section-label\">Exceptions</span>");
                    foreach (var e in docs.Exceptions)
                        sections.Append($"<span class=\"glosharp-popup-exception\"><span class=\"glosharp-popup-exception-type\">{Encode(e.Type)}</span> — {Encode(e.Text)}</span>");
                    sections.Append("</span>");
                }
            }

            if (sections.Length == 0) return;
            _sb.Append("<span class=\"glosharp-popup-docs\">").Append(sections).Append("</span>");
        }

        private void WriteErrorMessage(GloSharpError error)
        {
            var severity = NormalizeSeverity(error.Severity);
            var expected = error.Expected ? " glosharp-error-expected" : "";
            _sb.Append($"<span class=\"glosharp-callout glosharp-error-message glosharp-severity-{severity}{expected}\" role=\"note\">");
            _sb.Append(Icon(severity switch { "warning" => "warn", "info" => "log", _ => "error" }));
            _sb.Append("<span class=\"glosharp-callout-text\">");
            if (CsCodeRegex.IsMatch(error.Code))
            {
                var codeUrl = $"https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{error.Code.ToLowerInvariant()}";
                _sb.Append($"<a class=\"glosharp-error-code\" href=\"{Encode(codeUrl)}\" target=\"_blank\" rel=\"noopener\">{Encode(error.Code)}</a>");
            }
            else
            {
                _sb.Append($"<span class=\"glosharp-error-code\">{Encode(error.Code)}</span>");
            }
            _sb.Append($": {Encode(error.Message)}</span></span>");
        }

        private void WriteTag(GloSharpTag tag)
        {
            var name = TagNames.Contains(tag.Name) ? tag.Name : "log";
            _sb.Append($"<span class=\"glosharp-callout glosharp-tag glosharp-tag-{name}\" role=\"note\">");
            _sb.Append(Icon(name));
            _sb.Append($"<span class=\"glosharp-callout-text\"><span class=\"glosharp-tag-title\">{Encode(tag.Name)}:</span> {Encode(tag.Text)}</span>");
            _sb.Append("</span>");
        }

        private void WriteCompletionList(GloSharpCompletion completion)
        {
            _sb.Append($"<span class=\"glosharp-callout glosharp-completion\" style=\"--glosharp-column:{Math.Max(completion.Character, 0)}ch\">");
            // Focusable so a keyboard can scroll a long list.
            _sb.Append("<span class=\"glosharp-completion-list\" tabindex=\"0\">");
            if (completion.Items.Count == 0)
            {
                _sb.Append("<span class=\"glosharp-completion-empty\">No completions</span>");
            }
            foreach (var item in completion.Items)
            {
                _sb.Append("<span class=\"glosharp-completion-item\">");
                _sb.Append($"<span class=\"glosharp-completion-kind\">{Encode(item.Kind)}</span>");
                _sb.Append($"<span class=\"glosharp-completion-label\">{Encode(item.Label)}</span>");
                if (item.Detail != null)
                    _sb.Append($"<span class=\"glosharp-completion-detail\">{Encode(item.Detail)}</span>");
                _sb.Append("</span>");
            }
            _sb.Append("</span></span>");
        }

        private void AddUnderlines(GloSharpError error)
        {
            var rank = SeverityRank(error.Severity);
            var last = _lines.Length - 1;
            if (error.Line < 0 || error.Line > last) return;

            var endLine = Math.Min(error.EndLine ?? error.Line, last);
            if (endLine <= error.Line)
            {
                Bucket(_underlinesByLine, error.Line, (error.Character, error.Character + Math.Max(error.Length, 0), rank));
                return;
            }

            // A multi-line diagnostic: from its start to the end of the first line,
            // then each later line from its indentation, ending at EndCharacter.
            Bucket(_underlinesByLine, error.Line, (error.Character, _lines[error.Line].Length, rank));
            for (var l = error.Line + 1; l <= endLine; l++)
            {
                var text = _lines[l];
                var indent = text.Length - text.TrimStart().Length;
                var end = l == endLine && error.EndCharacter is { } ec ? ec : text.Length;
                if (end > indent)
                    Bucket(_underlinesByLine, l, (indent, end, rank));
            }
        }

        private static void Bucket<T>(Dictionary<int, List<T>> map, int key, T value)
        {
            if (!map.TryGetValue(key, out var list))
                map[key] = list = [];
            list.Add(value);
        }
    }

    private static readonly Regex CsCodeRegex = new(@"^CS\d+$", RegexOptions.Compiled);

    private static readonly HashSet<string> TagNames = ["log", "warn", "error", "annotate"];

    private static int SeverityRank(string severity) => NormalizeSeverity(severity) switch
    {
        "info" => 1,
        "warning" => 2,
        _ => 3,
    };

    private static string SeverityClass(int rank) => rank switch
    {
        1 => "glosharp-severity-info",
        2 => "glosharp-severity-warning",
        _ => "glosharp-severity-error",
    };

    private static string NormalizeSeverity(string severity) => severity switch
    {
        "warning" or "info" => severity,
        _ => "error",
    };

    // 32x32 icons shared with the Expressive Code plugin's callouts.
    private static readonly Dictionary<string, string> IconPaths = new()
    {
        ["log"] = "M16 2a14 14 0 1 0 14 14A14 14 0 0 0 16 2zm0 6a1.5 1.5 0 1 1-1.5 1.5A1.5 1.5 0 0 1 16 8zm4 16h-8v-2h3v-7h-2v-2h4v9h3z",
        ["warn"] = "M16.002 3a1 1 0 0 0-.866.5l-13 22.5A1 1 0 0 0 3 27.5h26a1 1 0 0 0 .866-1.5l-13-22.5a1 1 0 0 0-.864-.5zM15 13h2v8h-2zm1 12a1.5 1.5 0 1 1 0-3 1.5 1.5 0 0 1 0 3z",
        ["error"] = "M16 2a14 14 0 1 0 14 14A14 14 0 0 0 16 2zm5.8 18.4L20.4 21.8 16 17.4l-4.4 4.4-1.4-1.4L14.6 16l-4.4-4.4 1.4-1.4L16 14.6l4.4-4.4 1.4 1.4L17.4 16z",
        ["annotate"] = "M16 3a7 7 0 0 0-7 7c0 2.862 1.727 5.293 4.192 6.352A3.005 3.005 0 0 0 13 17v2a3 3 0 0 0 6 0v-2a3.005 3.005 0 0 0-.192-.648C21.273 15.293 23 12.862 23 10a7 7 0 0 0-7-7zm1 16a1 1 0 0 1-2 0v-2h2zm0-4h-2v-.736l-.434-.16C12.353 13.258 11 11.756 11 10a5 5 0 0 1 10 0c0 1.756-1.353 3.258-3.566 4.104L17 14.264zM13 24h6v2h-6z",
    };

    private static string Icon(string name) =>
        $"<svg class=\"glosharp-callout-icon\" aria-hidden=\"true\" focusable=\"false\" viewBox=\"0 0 32 32\" width=\"14\" height=\"14\" fill=\"currentColor\"><path d=\"{IconPaths[name]}\"/></svg>";

    private static string?[] ComputeLineClasses(int lineCount, List<GloSharpHighlight> highlights)
    {
        var classes = new string?[lineCount];
        var hasFocus = highlights.Any(h => h.Kind == "focus");
        var focusedLines = new HashSet<int>(highlights.Where(h => h.Kind == "focus").Select(h => h.Line));

        foreach (var h in highlights)
        {
            if (h.Line < 0 || h.Line >= lineCount) continue;
            classes[h.Line] = h.Kind switch
            {
                "highlight" => "glosharp-highlight",
                "add" => "glosharp-diff-add",
                "remove" => "glosharp-diff-remove",
                _ => classes[h.Line],
            };
        }

        if (hasFocus)
        {
            for (var i = 0; i < lineCount; i++)
            {
                if (!focusedLines.Contains(i) && classes[i] == null)
                    classes[i] = "glosharp-focus-dim";
            }
        }

        return classes;
    }

    /// <summary>
    /// Short, stable, document-unique prefix for this fragment's anchor names and ids.
    /// </summary>
    private static string ScopeId(string code, string themeName)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(themeName + "\n" + code));
        return $"gs{Convert.ToHexString(hash, 0, 4).ToLowerInvariant()}";
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
        "stringLiteral" => "string",
        "numericLiteral" => "number",
        _ => "text",
    };

    private static string PageStyles(GloSharpTheme theme) => $$"""
        html {
          color-scheme: {{theme.ColorScheme}};
          background: {{theme.Background}};
        }
        body {
          margin: 0;
          padding: 24px;
          color: {{theme.Foreground}};
          font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;
        }

        """;

    private static string BuildStylesheet(GloSharpTheme theme)
    {
        var r = $".glosharp-code[data-theme=\"{CssString(theme.Name)}\"]";
        var dark = theme.ColorScheme == "dark";
        var shadow = dark ? "0 6px 20px rgba(1, 4, 9, 0.6)" : "0 6px 20px rgba(31, 35, 40, 0.15)";
        var mono = "ui-monospace, 'Cascadia Code', 'JetBrains Mono', 'Fira Code', SFMono-Regular, Menlo, Consolas, monospace";
        var sans = "system-ui, -apple-system, 'Segoe UI', sans-serif";

        var sb = new StringBuilder();
        sb.Append($$"""
            {{r}} {
              anchor-scope: all;
              color-scheme: {{theme.ColorScheme}};
            }
            {{r}} pre {
              margin: 0;
              padding: 16px;
              overflow-x: auto;
              border-radius: 6px;
              background: {{theme.Background}};
              color: {{theme.Foreground}};
              font-family: {{mono}};
              font-size: 0.875rem;
              line-height: 1.6;
              font-variant-ligatures: none;
              tab-size: 4;
              text-align: left;
            }
            {{r}} pre > code {
              display: inline;
              padding: 0;
              border: 0;
              background: none;
              color: inherit;
              font: inherit;
              white-space: inherit;
            }
            {{r}} .line {
              display: inline;
            }
            {{r}} .glosharp-hover {
              position: static;
              border-bottom: 1px dotted transparent;
              border-radius: 2px;
            }
            {{r}} pre:hover .glosharp-hover,
            {{r}} .glosharp-hover-persistent {
              border-bottom-color: color-mix(in srgb, currentColor 45%, transparent);
            }
            {{r}} .glosharp-hover:hover,
            {{r}} .glosharp-hover:focus-visible {
              border-bottom-color: currentColor;
              background: color-mix(in srgb, {{theme.InfoColor}} 14%, transparent);
            }
            {{r}} .glosharp-hover:focus {
              outline: none;
            }
            {{r}} .glosharp-hover:focus-visible {
              outline: 2px solid {{theme.InfoColor}};
              outline-offset: 1px;
            }
            {{r}} .glosharp-popup {
              display: none;
              position: fixed;
              position-area: bottom span-right;
              position-try-fallbacks: bottom span-left, bottom span-all, top span-right, top span-left, top span-all;
              position-visibility: anchors-visible;
              z-index: 100;
              box-sizing: border-box;
              width: max-content;
              max-width: min(560px, calc(100vw - 16px));
              max-height: none;
              overflow: visible;
              margin: 6px 0;
              padding: 0;
              border: 1px solid {{theme.PopupBorder}};
              border-radius: 6px;
              background: {{theme.PopupBackground}};
              color: {{theme.PopupForeground}};
              box-shadow: {{shadow}};
              font-size: 90%;
              font-style: normal;
              font-weight: normal;
              line-height: 1.5;
              text-align: left;
              white-space: normal;
              cursor: auto;
            }
            {{r}} .glosharp-popup::before,
            {{r}} .glosharp-popup::after {
              content: "";
              position: absolute;
              left: 0;
              right: 0;
              height: 8px;
            }
            {{r}} .glosharp-popup::before {
              bottom: 100%;
            }
            {{r}} .glosharp-popup::after {
              top: 100%;
            }
            {{r}} .glosharp-hover:hover > .glosharp-popup,
            {{r}} .glosharp-hover:focus-visible > .glosharp-popup {
              display: block;
            }
            {{r}} .glosharp-popup-code {
              display: block;
              max-height: 12em;
              overflow-y: auto;
              padding: 6px 10px;
              background: none;
              color: inherit;
              font-family: {{mono}};
              font-size: inherit;
              white-space: pre-wrap;
              overflow-wrap: break-word;
            }
            {{r}} .glosharp-popup-overloads {
              opacity: 0.7;
            }
            {{r}} .glosharp-popup-types,
            {{r}} .glosharp-popup-docs {
              display: block;
              padding: 6px 10px;
              border-top: 1px solid {{theme.PopupBorder}};
            }
            {{r}} .glosharp-popup-docs {
              max-height: min(24em, 45vh);
              overflow-y: auto;
              font-family: {{sans}};
            }
            {{r}} .glosharp-type-annotation,
            {{r}} .glosharp-popup-summary,
            {{r}} .glosharp-popup-params,
            {{r}} .glosharp-popup-returns,
            {{r}} .glosharp-popup-remarks,
            {{r}} .glosharp-popup-example,
            {{r}} .glosharp-popup-exceptions,
            {{r}} .glosharp-popup-param,
            {{r}} .glosharp-popup-exception,
            {{r}} .glosharp-popup-section-label {
              display: block;
            }
            {{r}} .glosharp-popup-params,
            {{r}} .glosharp-popup-returns,
            {{r}} .glosharp-popup-remarks,
            {{r}} .glosharp-popup-example,
            {{r}} .glosharp-popup-exceptions {
              margin-top: 6px;
            }
            {{r}} .glosharp-popup-remarks {
              white-space: pre-line;
            }
            {{r}} .glosharp-popup-section-label {
              margin-bottom: 1px;
              font-size: 0.75em;
              font-weight: 600;
              letter-spacing: 0.05em;
              text-transform: uppercase;
              opacity: 0.7;
            }
            {{r}} .glosharp-popup-param,
            {{r}} .glosharp-popup-exception {
              margin: 1px 0;
            }
            {{r}} .glosharp-popup-param-name,
            {{r}} .glosharp-popup-exception-type,
            {{r}} .glosharp-type-annotation {
              font-family: {{mono}};
            }
            {{r}} .glosharp-popup-param-name,
            {{r}} .glosharp-popup-exception-type {
              font-weight: 600;
            }
            {{r}} .glosharp-popup-example-code {
              display: block;
              margin-top: 2px;
              padding: 4px 6px;
              border-radius: 4px;
              background: color-mix(in srgb, {{theme.PopupForeground}} 8%, transparent);
              font-family: {{mono}};
              white-space: pre-wrap;
            }
            {{r}} .glosharp-callout {
              display: block;
              margin: 2px 0 4px;
              white-space: pre-wrap;
              user-select: none;
              -webkit-user-select: none;
            }
            {{r}} .glosharp-static,
            {{r}} .glosharp-completion {
              padding-left: min(var(--glosharp-column, 0ch), max(0px, 100% - 34ch));
            }
            {{r}} .glosharp-static-container {
              display: inline-block;
              position: relative;
              max-width: 100%;
              box-sizing: border-box;
              margin-top: 4px;
              vertical-align: top;
              border: 1px solid {{theme.PopupBorder}};
              border-radius: 6px;
              background: {{theme.PopupBackground}};
              color: {{theme.PopupForeground}};
              font-size: 90%;
              line-height: 1.5;
              white-space: normal;
            }
            {{r}} .glosharp-static-container::before {
              content: "";
              position: absolute;
              top: -5px;
              left: 8px;
              width: 8px;
              height: 8px;
              border-top: 1px solid {{theme.PopupBorder}};
              border-left: 1px solid {{theme.PopupBorder}};
              background: {{theme.PopupBackground}};
              transform: rotate(45deg);
            }
            {{r}} .glosharp-error-message,
            {{r}} .glosharp-tag {
              display: flex;
              gap: 8px;
              align-items: flex-start;
              padding: 4px 10px;
              border-left: 3px solid;
              border-radius: 0 4px 4px 0;
              font-size: 0.875em;
              line-height: 1.5;
            }
            {{r}} .glosharp-callout-icon {
              flex: none;
              margin-top: 0.2em;
            }
            {{r}} .glosharp-callout-text {
              min-width: 0;
            }
            {{r}} .glosharp-error-code {
              font-weight: 600;
            }
            {{r}} a.glosharp-error-code {
              color: inherit;
              text-decoration: none;
            }
            {{r}} a.glosharp-error-code:hover,
            {{r}} a.glosharp-error-code:focus-visible {
              text-decoration: underline;
            }
            {{r}} .glosharp-tag-title {
              font-weight: 600;
            }
            {{r}} .glosharp-error-underline {
              text-decoration-line: underline;
              text-decoration-style: wavy;
              text-decoration-thickness: 1.5px;
              text-decoration-skip-ink: none;
              text-underline-offset: 3px;
            }
            {{r}} .glosharp-error-marker::before {
              content: "\a0\a0";
            }

            """);

        foreach (var severity in new[] { "error", "warning", "info" })
        {
            var (color, background) = theme.GetSeverityColors(severity);
            sb.Append($$"""
                {{r}} .glosharp-error-underline.glosharp-severity-{{severity}} {
                  text-decoration-color: {{color}};
                }
                {{r}} .glosharp-error-message.glosharp-severity-{{severity}} {
                  border-left-color: {{color}};
                  background: {{background}};
                  color: {{color}};
                }

                """);
        }

        foreach (var (tag, color, background) in new[]
                 {
                     ("log", theme.InfoColor, theme.InfoBackground),
                     ("warn", theme.WarningColor, theme.WarningBackground),
                     ("error", theme.ErrorColor, theme.ErrorBackground),
                     ("annotate", theme.AnnotateColor, theme.AnnotateBackground),
                 })
        {
            sb.Append($$"""
                {{r}} .glosharp-tag-{{tag}} {
                  border-left-color: {{color}};
                  background: {{background}};
                  color: {{color}};
                }

                """);
        }

        sb.Append($$"""
            {{r}} .glosharp-completion-list {
              display: inline-block;
              box-sizing: border-box;
              min-width: 18ch;
              max-width: 100%;
              max-height: 13em;
              overflow-y: auto;
              margin-top: 2px;
              padding: 3px 0;
              vertical-align: top;
              border: 1px solid {{theme.PopupBorder}};
              border-radius: 6px;
              background: {{theme.PopupBackground}};
              color: {{theme.PopupForeground}};
              box-shadow: {{shadow}};
              font-size: 90%;
              line-height: 1.5;
              white-space: nowrap;
            }
            {{r}} .glosharp-completion-list:focus-visible {
              outline: 2px solid {{theme.InfoColor}};
            }
            {{r}} .glosharp-completion-item,
            {{r}} .glosharp-completion-empty {
              display: flex;
              gap: 10px;
              align-items: baseline;
              padding: 0 10px;
            }
            {{r}} .glosharp-completion-item:first-child {
              background: color-mix(in srgb, {{theme.InfoColor}} 18%, transparent);
            }
            {{r}} .glosharp-completion-empty {
              font-style: italic;
              opacity: 0.7;
            }
            {{r}} .glosharp-completion-kind {
              flex: none;
              width: 10ch;
              overflow: hidden;
              text-overflow: ellipsis;
              font-size: 0.8em;
              opacity: 0.65;
            }
            {{r}} .glosharp-completion-detail {
              margin-left: auto;
              font-size: 0.85em;
              opacity: 0.65;
            }
            {{r}} .glosharp-highlight {
              background: {{theme.HighlightBackground}};
            }
            {{r}} .glosharp-focus-dim {
              opacity: {{theme.FocusDimOpacity}};
              transition: opacity 0.2s;
            }
            {{r}} pre:hover .glosharp-focus-dim {
              opacity: 1;
            }
            {{r}} .glosharp-diff-add {
              background: {{theme.DiffAddBackground}};
              box-shadow: -3px 0 {{theme.DiffAddBorder}};
            }
            {{r}} .glosharp-diff-remove {
              background: {{theme.DiffRemoveBackground}};
              box-shadow: -3px 0 {{theme.DiffRemoveBorder}};
            }
            @media (prefers-reduced-motion: reduce) {
              {{r}} .glosharp-focus-dim {
                transition: none;
              }
            }
            @supports not (anchor-name: --x) {
              {{r}} .glosharp-hover {
                position: relative;
              }
              {{r}} .glosharp-popup {
                position: absolute;
                top: 100%;
                left: 0;
              }
            }

            """);

        return sb.ToString();
    }

    /// <summary>Escapes a value for a double-quoted CSS string.</summary>
    private static string CssString(string value) =>
        value.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("<", "\\3c ");

    private static string Encode(string text) => WebUtility.HtmlEncode(text);
}
