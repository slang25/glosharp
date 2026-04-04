using GloSharp.Core;

namespace GloSharp.Tests;

public class HtmlRendererTests
{
    private static GloSharpResult CreateSimpleResult(
        string code = "var x = 42;",
        List<GloSharpHover>? hovers = null,
        List<GloSharpError>? errors = null,
        List<GloSharpHighlight>? highlights = null,
        List<GloSharpCompletion>? completions = null)
    {
        return new GloSharpResult
        {
            Code = code,
            Original = code,
            Hovers = hovers ?? [],
            Errors = errors ?? [],
            Completions = completions ?? [],
            Highlights = highlights ?? [],
            Meta = new GloSharpMeta
            {
                TargetFramework = "net8.0",
                CompileSucceeded = true,
            },
        };
    }

    private static List<ClassifiedToken> CreateSimpleTokens(string code)
    {
        // Create per-character tokens for test simplicity
        var tokens = new List<ClassifiedToken>();
        for (var i = 0; i < code.Length; i++)
        {
            if (code[i] == '\n') continue; // newlines are between lines
            tokens.Add(new ClassifiedToken(i, 1, "text", code[i].ToString()));
        }
        return tokens;
    }

    [Test]
    public async Task Render_BasicStructure_ContainsRequiredElements()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("<div class=\"glosharp-code\" data-theme=\"github-dark\">");
        await Assert.That(html).Contains("<style>");
        await Assert.That(html).Contains("<pre");
        await Assert.That(html).Contains("<code>");
        await Assert.That(html).DoesNotContain("<script");
    }

    [Test]
    public async Task Render_Standalone_WrapsInHtmlPage()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark, new HtmlRenderOptions { Standalone = true });

        await Assert.That(html).Contains("<!DOCTYPE html>");
        await Assert.That(html).Contains("<html");
        await Assert.That(html).Contains("<head>");
        await Assert.That(html).Contains("<body>");
        await Assert.That(html).Contains("</html>");
    }

    [Test]
    public async Task Render_Fragment_NoPageWrapper()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).DoesNotContain("<!DOCTYPE html>");
        await Assert.That(html).DoesNotContain("<html");
    }

    [Test]
    public async Task Render_WithHover_CreatesAnchorAndPopup()
    {
        var hover = new GloSharpHover
        {
            Line = 0, Character = 4, Length = 1,
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
            SymbolKind = "Local",
            TargetText = "x",
        };
        var result = CreateSimpleResult(hovers: [hover]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-hover");
        await Assert.That(html).Contains("anchor-name:--th-0");
        await Assert.That(html).Contains("glosharp-popup");
        await Assert.That(html).Contains("position-anchor:--th-0");
        await Assert.That(html).Contains("glosharp-popup-code");
    }

    [Test]
    public async Task Render_WithHoverDocs_RendersDocsSummary()
    {
        var hover = new GloSharpHover
        {
            Line = 0, Character = 0, Length = 1,
            Text = "test",
            Parts = [new GloSharpDisplayPart { Kind = "text", Text = "test" }],
            Docs = new GloSharpDocComment { Summary = "Gets the value." },
            SymbolKind = "Property",
            TargetText = "t",
        };
        var result = CreateSimpleResult(code: "t", hovers: [hover]);
        var tokens = CreateSimpleTokens("t");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-popup-docs");
        await Assert.That(html).Contains("Gets the value.");
    }

    [Test]
    public async Task Render_WithError_CreatesUnderlineAndMessage()
    {
        var error = new GloSharpError
        {
            Line = 0, Character = 0, Length = 3,
            Code = "CS1002", Message = "; expected",
            Severity = "error", Expected = false,
        };
        var result = CreateSimpleResult(errors: [error]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-error-underline");
        await Assert.That(html).Contains("glosharp-error-message");
        await Assert.That(html).Contains("CS1002");
        await Assert.That(html).Contains("; expected");
    }

    [Test]
    public async Task Render_WithHighlight_AppliesHighlightClass()
    {
        var highlight = new GloSharpHighlight { Line = 0, Character = 0, Length = 11, Kind = "highlight" };
        var result = CreateSimpleResult(highlights: [highlight]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-highlight");
    }

    [Test]
    public async Task Render_WithFocus_DimNonFocusedLines()
    {
        var code = "line1\nline2\nline3";
        var focus = new GloSharpHighlight { Line = 1, Character = 0, Length = 5, Kind = "focus" };
        var result = CreateSimpleResult(code: code, highlights: [focus]);
        var tokens = CreateSimpleTokens(code);
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-focus-dim");
    }

    [Test]
    public async Task Render_WithDiff_AppliesDiffClasses()
    {
        var code = "added\nremoved";
        var diffAdd = new GloSharpHighlight { Line = 0, Character = 0, Length = 5, Kind = "add" };
        var diffRemove = new GloSharpHighlight { Line = 1, Character = 0, Length = 7, Kind = "remove" };
        var result = CreateSimpleResult(code: code, highlights: [diffAdd, diffRemove]);
        var tokens = CreateSimpleTokens(code);
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-diff-add");
        await Assert.That(html).Contains("glosharp-diff-remove");
    }

    [Test]
    public async Task Render_WithCompletion_RendersCompletionList()
    {
        var completion = new GloSharpCompletion
        {
            Line = 0, Character = 0,
            Items =
            [
                new GloSharpCompletionItem { Label = "WriteLine", Kind = "Method" },
                new GloSharpCompletionItem { Label = "Write", Kind = "Method" },
            ],
        };
        var result = CreateSimpleResult(completions: [completion]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-completion-list");
        await Assert.That(html).Contains("WriteLine");
        await Assert.That(html).Contains("Write");
    }

    [Test]
    public async Task Render_CssContainsAnchorFallback()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("@supports not (anchor-name: --x)");
    }

    [Test]
    public async Task Render_CssContainsPopupShowHide()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains(".glosharp-popup {\n  display: none;");
        await Assert.That(html).Contains(".glosharp-hover:hover + .glosharp-popup");
    }

    [Test]
    public async Task Render_NoScriptTags()
    {
        var hover = new GloSharpHover
        {
            Line = 0, Character = 0, Length = 1, Text = "test",
            Parts = [new GloSharpDisplayPart { Kind = "text", Text = "test" }],
            SymbolKind = "Local", TargetText = "t",
        };
        var error = new GloSharpError
        {
            Line = 0, Character = 0, Length = 1,
            Code = "CS0001", Message = "test", Severity = "error", Expected = false,
        };
        var completion = new GloSharpCompletion
        {
            Line = 0, Character = 0,
            Items = [new GloSharpCompletionItem { Label = "test", Kind = "Method" }],
        };
        var result = CreateSimpleResult(code: "t", hovers: [hover], errors: [error], completions: [completion]);
        var tokens = CreateSimpleTokens("t");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).DoesNotContain("<script");
        await Assert.That(html).DoesNotContain("onclick");
        await Assert.That(html).DoesNotContain("onmouseover");
    }

    [Test]
    public async Task Render_LightTheme_UsesLightColors()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubLight);

        await Assert.That(html).Contains("data-theme=\"github-light\"");
        await Assert.That(html).Contains($"background:{GloSharpTheme.GithubLight.Background}");
    }

    [Test]
    public async Task Render_MultipleHovers_UniqueAnchors()
    {
        var hovers = new List<GloSharpHover>
        {
            new() { Line = 0, Character = 0, Length = 1, Text = "a", Parts = [], SymbolKind = "Local", TargetText = "a" },
            new() { Line = 0, Character = 5, Length = 1, Text = "b", Parts = [], SymbolKind = "Local", TargetText = "b" },
            new() { Line = 0, Character = 10, Length = 1, Text = "c", Parts = [], SymbolKind = "Local", TargetText = "c" },
        };
        var result = CreateSimpleResult(hovers: hovers);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("--th-0");
        await Assert.That(html).Contains("--th-1");
        await Assert.That(html).Contains("--th-2");
    }

    // === Richer error display tests ===

    [Test]
    public async Task Render_ErrorSeverity_AppliesSeverityClass()
    {
        var error = new GloSharpError
        {
            Line = 0, Character = 0, Length = 3,
            Code = "CS1002", Message = "; expected",
            Severity = "error", Expected = false,
        };
        var result = CreateSimpleResult(errors: [error]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-severity-error");
    }

    [Test]
    public async Task Render_WarningSeverity_AppliesWarningClass()
    {
        var warning = new GloSharpError
        {
            Line = 0, Character = 0, Length = 3,
            Code = "CS8600", Message = "Converting null to non-nullable",
            Severity = "warning", Expected = false,
        };
        var result = CreateSimpleResult(errors: [warning]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-severity-warning");
    }

    [Test]
    public async Task Render_CsErrorCode_RenderedAsLink()
    {
        var error = new GloSharpError
        {
            Line = 0, Character = 0, Length = 3,
            Code = "CS0246", Message = "type not found",
            Severity = "error", Expected = false,
        };
        var result = CreateSimpleResult(errors: [error]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("<a class=\"glosharp-error-code\" href=\"https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246\"");
        await Assert.That(html).Contains("target=\"_blank\"");
        await Assert.That(html).Contains("rel=\"noopener\"");
    }

    [Test]
    public async Task Render_AnalyzerCode_RenderedAsPlainText()
    {
        var error = new GloSharpError
        {
            Line = 0, Character = 0, Length = 3,
            Code = "CA1234", Message = "analyzer warning",
            Severity = "warning", Expected = false,
        };
        var result = CreateSimpleResult(errors: [error]);
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("<span class=\"glosharp-error-code\">CA1234</span>");
        await Assert.That(html).DoesNotContain("<a class=\"glosharp-error-code\"");
    }

    [Test]
    public async Task Render_CssContainsSeverityStyles()
    {
        var result = CreateSimpleResult();
        var tokens = CreateSimpleTokens("var x = 42;");
        var html = HtmlRenderer.Render(result, tokens, GloSharpTheme.GithubDark);

        await Assert.That(html).Contains("glosharp-severity-warning");
        await Assert.That(html).Contains("glosharp-severity-info");
        await Assert.That(html).Contains(GloSharpTheme.GithubDark.WarningColor);
        await Assert.That(html).Contains(GloSharpTheme.GithubDark.InfoColor);
    }
}
