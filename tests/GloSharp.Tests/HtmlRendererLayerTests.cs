using System.Text.RegularExpressions;
using GloSharp.Core;

namespace GloSharp.Tests;

/// <summary>
/// Diagnostics, callouts, accessibility, markup validity and CSS scoping in the
/// standalone renderer.
/// </summary>
public class HtmlRendererLayerTests
{
    private const string DarkRoot = ".glosharp-code[data-theme=\"github-dark\"]";

    private static GloSharpResult Result(
        string code,
        List<GloSharpHover>? hovers = null,
        List<GloSharpError>? errors = null,
        List<GloSharpCompletion>? completions = null,
        List<GloSharpTag>? tags = null) => new()
    {
        Code = code,
        Original = code,
        Hovers = hovers ?? [],
        Errors = errors ?? [],
        Completions = completions ?? [],
        Tags = tags ?? [],
        Meta = new GloSharpMeta { TargetFramework = "net8.0", CompileSucceeded = true },
    };

    /// <summary>Word-level tokens, so a range can span several of them.</summary>
    private static List<ClassifiedToken> WordTokens(string code)
    {
        var tokens = new List<ClassifiedToken>();
        foreach (Match m in Regex.Matches(code, @"\w+|[^\w\s]|[ \t]+"))
        {
            var kind = char.IsWhiteSpace(m.Value[0]) ? "whitespace"
                : m.Value is "var" or "int" or "string" ? "keyword"
                : char.IsDigit(m.Value[0]) ? "number"
                : char.IsLetter(m.Value[0]) ? "localName"
                : "punctuation";
            tokens.Add(new ClassifiedToken(m.Index, m.Length, kind, m.Value));
        }
        return tokens;
    }

    private static string Render(GloSharpResult result, HtmlRenderOptions? options = null) =>
        HtmlRenderer.Render(result, WordTokens(result.Code), GloSharpTheme.GithubDark, options);

    private static GloSharpError Error(int line, int character, int length, string code = "CS0029",
        string severity = "error", int? endLine = null, int? endCharacter = null, bool expected = false) => new()
    {
        Line = line, Character = character, Length = length,
        EndLine = endLine, EndCharacter = endCharacter,
        Code = code, Message = $"message for {code}", Severity = severity, Expected = expected,
    };

    private static GloSharpHover Hover(int line, int character, int length, bool persistent = false,
        GloSharpDocComment? docs = null, int? overloads = null) => new()
    {
        Line = line, Character = character, Length = length,
        Text = "(local variable) int x",
        Parts =
        [
            new GloSharpDisplayPart { Kind = "punctuation", Text = "(" },
            new GloSharpDisplayPart { Kind = "text", Text = "local variable" },
            new GloSharpDisplayPart { Kind = "punctuation", Text = ")" },
            new GloSharpDisplayPart { Kind = "space", Text = " " },
            new GloSharpDisplayPart { Kind = "keyword", Text = "int" },
            new GloSharpDisplayPart { Kind = "space", Text = " " },
            new GloSharpDisplayPart { Kind = "localName", Text = "x" },
        ],
        Docs = docs,
        SymbolKind = "Local",
        TargetText = "x",
        OverloadCount = overloads,
        Persistent = persistent,
    };

    /// <summary>The visible code text inside each underline span, in order.</summary>
    private static List<string> UnderlinedTexts(string html)
    {
        var texts = new List<string>();
        var marker = "<span class=\"glosharp-error-underline";
        var at = 0;
        while ((at = html.IndexOf(marker, at, StringComparison.Ordinal)) >= 0)
        {
            var open = html.IndexOf('>', at) + 1;
            var depth = 1;
            var i = open;
            while (depth > 0)
            {
                var nextOpen = html.IndexOf("<span", i, StringComparison.Ordinal);
                var nextClose = html.IndexOf("</span>", i, StringComparison.Ordinal);
                if (nextOpen >= 0 && nextOpen < nextClose) { depth++; i = nextOpen + 5; }
                else { depth--; i = nextClose + 7; }
            }
            var inner = html[open..(i - 7)];
            // Drop any popup nested in a hover inside the underline.
            inner = Regex.Replace(inner, "<span class=\"glosharp-popup\".*", "", RegexOptions.Singleline);
            texts.Add(System.Net.WebUtility.HtmlDecode(Regex.Replace(inner, "<[^>]+>", "")));
            at = open;
        }
        return texts;
    }

    private static string CodeBlock(string html)
    {
        var start = html.IndexOf("<pre>", StringComparison.Ordinal);
        var end = html.IndexOf("</pre>", StringComparison.Ordinal);
        return html[start..(end + 6)];
    }

    // === Error squiggles ===

    [Test]
    public async Task Css_SquiggleIsAWavyTextDecoration_NotAnInvalidBorder()
    {
        // `border-bottom: 2px wavy` is invalid CSS — `wavy` is not a border
        // style — so browsers dropped the whole declaration and no squiggle drew.
        var css = HtmlRenderer.GenerateStylesheet(GloSharpTheme.GithubDark);

        await Assert.That(css).DoesNotContain("2px wavy");
        await Assert.That(css).Contains("text-decoration-style: wavy;");
        await Assert.That(css).Contains($"text-decoration-color: {GloSharpTheme.GithubDark.ErrorColor};");
        await Assert.That(css).Contains($"text-decoration-color: {GloSharpTheme.GithubDark.WarningColor};");
    }

    [Test]
    public async Task Underline_CoversTheWholeRange_AcrossTokens()
    {
        // CS0029 on `"three"` starting mid-line and spanning several tokens: the
        // old renderer only underlined a token that began exactly at the error
        // column, and ignored Length.
        var code = "int count = 1 + 2;";
        var html = Render(Result(code, errors: [Error(0, 12, 5)]));

        await Assert.That(string.Concat(UnderlinedTexts(html))).IsEqualTo("1 + 2");
    }

    [Test]
    public async Task Underline_StartingInsideAToken_IsStillDrawn()
    {
        var code = "var total = items;";
        var html = Render(Result(code, errors: [Error(0, 14, 3)]));

        await Assert.That(string.Concat(UnderlinedTexts(html))).IsEqualTo("ems");
    }

    [Test]
    public async Task ZeroWidthErrorAtEndOfLine_UnderlinesThePrecedingToken()
    {
        // CS1002 "; expected" is reported as a zero-width span after the last
        // token, past the end of the line text.
        var code = "int x = 1\nint y = 2;";
        var html = Render(Result(code, errors: [Error(0, 9, 0, "CS1002")]));

        await Assert.That(UnderlinedTexts(html)).IsEquivalentTo(new[] { "1" });
        await Assert.That(html).Contains("CS1002");
    }

    [Test]
    public async Task ZeroWidthErrorAtLineStart_UnderlinesTheFollowingToken()
    {
        var code = "    Foo();";
        var html = Render(Result(code, errors: [Error(0, 0, 0)]));

        await Assert.That(UnderlinedTexts(html)).IsEquivalentTo(new[] { "Foo" });
    }

    [Test]
    public async Task ErrorOnABlankLine_DrawsAMarker()
    {
        var code = "var a = 1;\n\nvar b = 2;";
        var html = Render(Result(code, errors: [Error(1, 0, 0, "CS1513")]));

        await Assert.That(html).Contains("glosharp-error-marker glosharp-severity-error");
    }

    [Test]
    public async Task WarningOnAHoveredIdentifier_KeepsBothTheHoverAndTheSquiggle()
    {
        // `var a = 1;` has CS0219 on `a`, which also has a hover. Hover used to
        // win and the warning lost its squiggle.
        var code = "var a = 1;";
        var html = Render(Result(code,
            hovers: [Hover(0, 4, 1)],
            errors: [Error(0, 4, 1, "CS0219", "warning")]));

        var hoverAt = html.IndexOf("<span class=\"glosharp-hover\"", StringComparison.Ordinal);
        var underlineAt = html.IndexOf("<span class=\"glosharp-error-underline glosharp-severity-warning\">", StringComparison.Ordinal);
        var popupAt = html.IndexOf("<span class=\"glosharp-popup\"", StringComparison.Ordinal);

        await Assert.That(hoverAt).IsGreaterThan(-1);
        await Assert.That(underlineAt).IsGreaterThan(hoverAt);
        await Assert.That(underlineAt).IsLessThan(popupAt);
        await Assert.That(UnderlinedTexts(html)).IsEquivalentTo(new[] { "a" });
    }

    [Test]
    public async Task ErrorSpanningAHover_UnderlinesBothSidesOfIt()
    {
        var code = "Greet(name);";
        var html = Render(Result(code,
            hovers: [Hover(0, 6, 4)],
            errors: [Error(0, 0, 11)]));

        await Assert.That(string.Concat(UnderlinedTexts(html))).IsEqualTo("Greet(name)");
    }

    [Test]
    public async Task MultiLineError_UnderlinesEachLineFromItsIndentation_AndShowsTheMessageOnce()
    {
        var code = "Call(\n    a,\n    b);\nnext();";
        var html = Render(Result(code, errors: [Error(0, 0, 0, "CS7036", endLine: 2, endCharacter: 6)]));

        await Assert.That(UnderlinedTexts(html)).IsEquivalentTo(new[] { "Call(", "a,", "b)" });
        var block = CodeBlock(html);
        await Assert.That(Regex.Matches(block, "glosharp-error-message ").Count).IsEqualTo(1);

        // The message sits after the last line of the span, before the next line.
        var message = block.IndexOf("glosharp-error-message", StringComparison.Ordinal);
        await Assert.That(message).IsGreaterThan(block.IndexOf(">b<", StringComparison.Ordinal));
        await Assert.That(message).IsLessThan(block.IndexOf("next", StringComparison.Ordinal));
    }

    [Test]
    public async Task ErrorMessage_IsRenderedInsideTheCodeBlock_UnderItsLine()
    {
        var code = "int count = \"three\";\nvar ok = 1;";
        var html = Render(Result(code, errors: [Error(0, 12, 7)]));
        var block = CodeBlock(html);

        await Assert.That(block).Contains("glosharp-error-message");
        var message = block.IndexOf("glosharp-error-message", StringComparison.Ordinal);
        await Assert.That(message).IsGreaterThan(block.IndexOf("three", StringComparison.Ordinal));
        await Assert.That(message).IsLessThan(block.IndexOf("ok", StringComparison.Ordinal));
    }

    [Test]
    public async Task ExpectedError_IsMarked()
    {
        var html = Render(Result("int x = \"s\";", errors: [Error(0, 8, 3, expected: true)]));

        await Assert.That(html).Contains("glosharp-error-expected");
    }

    [Test]
    public async Task HiddenDiagnostic_IsNotRendered()
    {
        var html = Render(Result("var a = 1;", errors: [Error(0, 4, 1, "IDE0059", "hidden")]));

        await Assert.That(CodeBlock(html)).DoesNotContain("glosharp-error");
    }

    // === Callouts ===

    [Test]
    public async Task CustomTags_RenderAsCalloutsAfterTheirLine()
    {
        var code = "var cache = 1;\ncache = 2;";
        var html = Render(Result(code, tags:
        [
            new GloSharpTag { Name = "log", Text = "Cache starts empty", Line = 0 },
            new GloSharpTag { Name = "warn", Text = "Overwrites silently", Line = 1 },
            new GloSharpTag { Name = "error", Text = "Throws", Line = 1 },
            new GloSharpTag { Name = "annotate", Text = "O(1)", Line = 1 },
        ]));

        foreach (var name in new[] { "log", "warn", "error", "annotate" })
            await Assert.That(html).Contains($"glosharp-tag glosharp-tag-{name}");
        await Assert.That(html).Contains("Cache starts empty");

        // @log belongs to line 0: its callout comes before line 1 starts.
        var block = CodeBlock(html);
        var secondLine = block.LastIndexOf("<span class=\"line\">", StringComparison.Ordinal);
        await Assert.That(block.IndexOf("glosharp-tag-log", StringComparison.Ordinal)).IsLessThan(secondLine);
        await Assert.That(block.IndexOf("glosharp-tag-warn", StringComparison.Ordinal)).IsGreaterThan(secondLine);
    }

    [Test]
    public async Task PersistentQuery_RendersAVisibleCalloutUnderItsToken()
    {
        var code = "var x = 42;\nvar y = x;";
        var html = Render(Result(code, hovers:
        [
            Hover(0, 4, 1, persistent: true, docs: new GloSharpDocComment
            {
                Summary = "The answer.",
                Params = [new GloSharpDocParam { Name = "p", Text = "unused" }],
            }),
        ]));

        await Assert.That(html).Contains("glosharp-hover glosharp-hover-persistent");
        await Assert.That(html).Contains("<span class=\"glosharp-callout glosharp-static\" style=\"--glosharp-column:4ch\">");

        // The callout is compact: signature and summary; the full docs stay in the popup.
        var callout = html[html.IndexOf("glosharp-callout glosharp-static", StringComparison.Ordinal)..];
        callout = callout[..callout.IndexOf("<span class=\"line\">", StringComparison.Ordinal)];
        await Assert.That(callout).Contains("The answer.");
        await Assert.That(callout).DoesNotContain("glosharp-popup-params");

        // Placed after line 0, before line 1.
        var calloutAt = html.IndexOf("glosharp-static", StringComparison.Ordinal);
        await Assert.That(calloutAt).IsLessThan(html.LastIndexOf("<span class=\"line\">", StringComparison.Ordinal));
    }

    [Test]
    public async Task Popup_RendersEveryDocSection_AndTheOverloadCount()
    {
        var docs = new GloSharpDocComment
        {
            Summary = "Converts text.",
            Params = [new GloSharpDocParam { Name = "value", Text = "The input." }],
            Returns = "The converted text.",
            Remarks = "Culture-sensitive.",
            Examples = ["var s = Convert(\"a\");"],
            Exceptions = [new GloSharpDocException { Type = "ArgumentNullException", Text = "value is null." }],
        };
        var hover = Hover(0, 4, 1, docs: docs, overloads: 20);
        var withTypes = new GloSharpHover
        {
            Line = hover.Line, Character = hover.Character, Length = hover.Length, Text = hover.Text,
            Parts = hover.Parts, Docs = hover.Docs, SymbolKind = hover.SymbolKind, TargetText = hover.TargetText,
            OverloadCount = hover.OverloadCount,
            TypeAnnotations = [new GloSharpTypeAnnotation { Name = "'a", Expansion = "new { int X }" }],
        };
        var html = Render(Result("var x = 42;", hovers: [withTypes]));

        await Assert.That(html).Contains("Converts text.");
        await Assert.That(html).Contains("glosharp-popup-params");
        await Assert.That(html).Contains("The converted text.");
        await Assert.That(html).Contains("glosharp-popup-remarks");
        await Assert.That(html).Contains("Culture-sensitive.");
        await Assert.That(html).Contains("glosharp-popup-example-code");
        await Assert.That(html).Contains("glosharp-popup-exceptions");
        await Assert.That(html).Contains("ArgumentNullException");
        await Assert.That(html).Contains("(+19 overloads)");
        await Assert.That(html).Contains("glosharp-popup-types");
        await Assert.That(html).Contains("new { int X }");
    }

    [Test]
    public async Task SingleOtherOverload_IsSingular()
    {
        var html = Render(Result("var x = 42;", hovers: [Hover(0, 4, 1, overloads: 2)]));

        await Assert.That(html).Contains("(+1 overload)");
    }

    [Test]
    public async Task Completions_RenderAtTheirColumn_AndSayWhenEmpty()
    {
        var code = "sb.App\nlist.";
        var html = Render(Result(code, completions:
        [
            new GloSharpCompletion
            {
                Line = 0, Character = 6,
                Items = [new GloSharpCompletionItem { Label = "Append", Kind = "Method", Detail = "StringBuilder" }],
            },
            new GloSharpCompletion { Line = 1, Character = 5, Items = [] },
        ]));

        await Assert.That(html).Contains("--glosharp-column:6ch");
        await Assert.That(html).Contains("<span class=\"glosharp-completion-label\">Append</span>");
        await Assert.That(html).Contains("No completions");
    }

    // === Markup validity and accessibility ===

    [Test]
    public async Task CodeBlock_ContainsOnlyPhrasingContent()
    {
        // <div>, <ul> and <pre> are not permitted inside <code> (or a <span>);
        // html-validate flagged every popup.
        var code = "var x = 42;\nx = 1";
        var html = Render(Result(code,
            hovers: [Hover(0, 4, 1, docs: new GloSharpDocComment { Summary = "s", Examples = ["e"] }), Hover(1, 0, 1, persistent: true)],
            errors: [Error(1, 5, 0, "CS1002")],
            completions: [new GloSharpCompletion { Line = 1, Character = 1, Items = [new GloSharpCompletionItem { Label = "a", Kind = "Method" }] }],
            tags: [new GloSharpTag { Name = "log", Text = "t", Line = 0 }]));

        var block = CodeBlock(html);
        foreach (var tag in new[] { "<div", "<ul", "<li", "<p>", "<pre", "<section" })
            await Assert.That(block[5..]).DoesNotContain(tag);
    }

    [Test]
    public async Task HoverTargets_AreKeyboardFocusable_AndDescribedByTheirTooltip()
    {
        var html = Render(Result("var x = 42;", hovers: [Hover(0, 4, 1)]));

        var hover = Regex.Match(html, "<span class=\"glosharp-hover\" tabindex=\"0\" aria-describedby=\"([^\"]+)\"");
        await Assert.That(hover.Success).IsTrue();
        var id = hover.Groups[1].Value;
        await Assert.That(html).Contains($"<span class=\"glosharp-popup\" role=\"tooltip\" id=\"{id}\"");
    }

    [Test]
    public async Task Css_ShowsPopupsOnKeyboardFocus_WithAVisibleFocusRing()
    {
        var css = HtmlRenderer.GenerateStylesheet(GloSharpTheme.GithubDark);

        await Assert.That(css).Contains($"{DarkRoot} .glosharp-hover:focus-visible > .glosharp-popup");
        await Assert.That(css).Contains($"{DarkRoot} .glosharp-hover:focus-visible {{\n  outline: 2px solid");
    }

    [Test]
    public async Task Css_PopupsOpenBelow_AndFlipWhenThereIsNoRoom()
    {
        // `position-area: top` with no fallback slid popups for tokens near the
        // top of the viewport down over the very code they describe.
        var css = HtmlRenderer.GenerateStylesheet(GloSharpTheme.GithubDark);

        await Assert.That(css).Contains("position-area: bottom span-right;");
        await Assert.That(css).Contains("position-try-fallbacks: bottom span-left, bottom span-all, top span-right, top span-left, top span-all;");
        await Assert.That(css).Contains("max-height:");
    }

    [Test]
    public async Task Standalone_KeepsTheStylesheetInHead()
    {
        // <style> is only valid in <head>.
        var html = Render(Result("var x = 42;"), new HtmlRenderOptions { Standalone = true, Title = "Example <1>" });

        var head = html[..html.IndexOf("</head>", StringComparison.Ordinal)];
        var body = html[html.IndexOf("<body>", StringComparison.Ordinal)..];
        await Assert.That(head).Contains("<style>");
        await Assert.That(body).DoesNotContain("<style>");
        await Assert.That(html).Contains("<title>Example &lt;1&gt;</title>");
    }

    [Test]
    public async Task IncludeStylesFalse_EmitsNoStylesheet()
    {
        var html = Render(Result("var x = 42;"), new HtmlRenderOptions { IncludeStyles = false });

        await Assert.That(html).DoesNotContain("<style>");
        await Assert.That(html).StartsWith("<div class=\"glosharp-code\" data-theme=\"github-dark\">");
    }

    [Test]
    public async Task FinalNewline_LeavesNoEmptyLineElement()
    {
        var html = Render(Result("var x = 42;\n"));

        await Assert.That(html).DoesNotContain("<span class=\"line\"></span>");
        await Assert.That(html).Contains("var</span> x = <span");
    }

    [Test]
    public async Task SameCodeInTwoThemes_GetsDistinctIds()
    {
        // A light/dark pair of one snippet can share a page (theme switchers do).
        var result = Result("var x = 42;", hovers: [Hover(0, 4, 1)]);
        var dark = HtmlRenderer.Render(result, WordTokens(result.Code), GloSharpTheme.GithubDark);
        var light = HtmlRenderer.Render(result, WordTokens(result.Code), GloSharpTheme.GithubLight);

        string Id(string html) => Regex.Match(html, "aria-describedby=\"([^\"]+)\"").Groups[1].Value;
        await Assert.That(Id(dark)).IsNotEqualTo(Id(light));
    }

    // === CSS scoping ===

    [Test]
    public async Task Css_EverySelectorIsScopedToItsThemeRoot()
    {
        // An unscoped `.glosharp-popup` or `.glosharp-completion-list` rule in one
        // fragment restyled other glosharp output (and other themes) on the page.
        foreach (var theme in new[] { GloSharpTheme.GithubDark, GloSharpTheme.GithubLight })
        {
            var root = $".glosharp-code[data-theme=\"{theme.Name}\"]";
            var css = HtmlRenderer.GenerateStylesheet(theme);
            var selectors = Regex.Matches(css, @"([^{}]+)\{")
                .Select(m => m.Groups[1].Value.Trim())
                .Where(s => !s.StartsWith('@'))
                .SelectMany(s => s.Split(','))
                .Select(s => s.Trim())
                .ToList();

            await Assert.That(selectors.Count).IsGreaterThan(20);
            foreach (var selector in selectors)
                await Assert.That(selector).StartsWith(root);
        }
    }

    [Test]
    public async Task Css_HasNoComments()
    {
        var css = HtmlRenderer.GenerateStylesheet(GloSharpTheme.GithubDark);

        await Assert.That(css).DoesNotContain("/*");
    }
}
