using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

/// <summary>A snippet's compilation and the settings it was built with.</summary>
/// <param name="Tree">The snippet's own syntax tree (the other tree holds the global usings).</param>
/// <param name="GlobalUsings">The text of the global usings tree.</param>
internal sealed record SnippetCompilation(
    CSharpCompilation Compilation,
    SyntaxTree Tree,
    string GlobalUsings,
    LanguageVersion LangVersion,
    NullableContextOptions Nullable,
    CompilationContext Context);

/// <summary>
/// Builds the <see cref="CSharpCompilation"/> for a snippet from its compilation context and the
/// requested language version / nullable context / implicit usings.
/// </summary>
internal static class CompilationBuilder
{
    internal const string GlobalUsingsPath = "__GlobalUsings.cs";

    private static readonly string[] DefaultGlobalUsings =
    [
        "System",
        "System.Collections.Generic",
        "System.IO",
        "System.Linq",
        "System.Net.Http",
        "System.Threading",
        "System.Threading.Tasks",
    ];

    private static readonly string[] WebSdkGlobalUsings =
    [
        "System.Net.Http.Json",
        "Microsoft.AspNetCore.Builder",
        "Microsoft.AspNetCore.Http",
        "Microsoft.AspNetCore.Hosting",
        "Microsoft.AspNetCore.Routing",
        "Microsoft.Extensions.Configuration",
        "Microsoft.Extensions.DependencyInjection",
        "Microsoft.Extensions.Hosting",
        "Microsoft.Extensions.Logging",
    ];

    /// <param name="implicitUsings">
    /// Configured global usings; replaces the defaults (and the Web SDK set) when non-null.
    /// </param>
    public static SnippetCompilation Build(
        Snippet snippet,
        CompilationContext context,
        RequestedSettings requested,
        string[]? implicitUsings)
    {
        var globalUsings = BuildGlobalUsings(implicitUsings, context.IsWeb);

        // Settings the snippet doesn't ask for come from the context (the project's own options
        // for a complog, else latest / enable)
        var langVersion = requested.LangVersion ?? context.ParseOptions.SpecifiedLanguageVersion;
        var nullable = requested.Nullable ?? context.CompilationOptions.NullableContextOptions;

        var parseOptions = context.ParseOptions.WithLanguageVersion(langVersion);
        var compilationOptions = context.CompilationOptions.WithNullableContextOptions(nullable);

        var globalUsingsTree = CSharpSyntaxTree.ParseText(globalUsings, parseOptions, path: GlobalUsingsPath);
        var tree = CSharpSyntaxTree.ParseText(snippet.Markers.CompilationCode, parseOptions);

        var compilation = CSharpCompilation.Create(
            "GloSharpSnippet",
            [tree, globalUsingsTree],
            context.References,
            compilationOptions);

        return new SnippetCompilation(compilation, tree, globalUsings, langVersion, nullable, context);
    }

    /// <summary>
    /// The global usings file: the configured usings, else the defaults plus, for web contexts,
    /// the Web SDK's.
    /// </summary>
    internal static string BuildGlobalUsings(string[]? implicitUsings, bool isWeb)
    {
        IEnumerable<string> usings = implicitUsings ?? DefaultGlobalUsings;
        if (implicitUsings == null && isWeb)
            usings = usings.Concat(WebSdkGlobalUsings);

        return string.Join('\n', usings
            .Where(u => !string.IsNullOrWhiteSpace(u))
            .Distinct(StringComparer.Ordinal)
            .Select(u => $"global using {u};"));
    }
}
