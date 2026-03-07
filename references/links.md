# Annotated links

## Twoslash ecosystem

- [twoslash docs](https://twoslash.netlify.app/) — main documentation site, covers markup syntax and output format
- [twoslash GitHub](https://github.com/twoslashes/twoslash) — monorepo with core, shiki integration, and CDN package
- [shiki-twoslash](https://shiki.style/packages/twoslash) — Shiki's official twoslash transformer docs
- [twoslash core source](https://github.com/twoslashes/twoslash/tree/main/packages/twoslash/src) — the actual implementation, good reference for data model

## Roslyn / C# compiler platform

- [Roslyn SDK overview](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/) — entry point for Roslyn documentation
- [Semantic analysis tutorial](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/get-started/semantic-analysis) — walkthrough of SemanticModel usage
- [Work with semantics](https://learn.microsoft.com/en-us/dotnet/csharp/roslyn-sdk/work-with-semantics) — deeper guide to Compilation, symbols, and semantic model
- [SemanticModel API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.semanticmodel) — full API reference
- [ISymbol API](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.isymbol) — symbol interface hierarchy
- [SymbolDisplayFormat](https://learn.microsoft.com/en-us/dotnet/api/microsoft.codeanalysis.symboldisplayformat) — controls how types are rendered as strings
- [OmniSharp source](https://github.com/OmniSharp/omnisharp-roslyn) — reference implementation for C# language server features
- [Route to Roslyn](https://route2roslyn.netlify.app/) — community guide with practical examples

## Shiki (syntax highlighter)

- [Shiki docs](https://shiki.style/) — main documentation site
- [Shiki transformers guide](https://shiki.style/guide/transformers) — how to write custom transformers
- [Shiki transformers package](https://shiki.style/packages/transformers) — built-in transformers (diff, highlight, focus, etc.)
- [Shiki GitHub](https://github.com/shikijs/shiki) — source code, especially packages/twoslash for the integration pattern
- [HAST spec](https://github.com/syntax-tree/hast) — Hypertext Abstract Syntax Tree, Shiki's output format

## Expressive Code

- [Expressive Code docs](https://expressive-code.com/) — main site with guides and API reference
- [EC plugin development guide](https://expressive-code.com/guides/developing-plugins/) — how to build plugins
- [EC plugin API reference](https://expressive-code.com/reference/plugin-api/) — full API surface
- [EC plugin hooks](https://expressive-code.com/reference/plugin-hooks/) — available hook points

## Documentation frameworks

- [Starlight (Astro)](https://starlight.astro.build/) — Astro's docs framework, EC built-in
- [VitePress](https://vitepress.dev/) — Vue-powered SSG, Shiki built-in
- [Docusaurus code blocks](https://docusaurus.io/docs/markdown-features/code-blocks) — code snippet features
- [Docfx](https://dotnet.github.io/docfx/) — Microsoft's .NET documentation tool

## CSS / rendering

- [CSS anchor positioning (MDN)](https://developer.mozilla.org/en-US/docs/Web/CSS/CSS_anchor_positioning) — modern tooltip positioning, no JS needed
- [CSS anchor positioning guide](https://css-tip.com/tooltip-anchor/) — practical examples and patterns
- [Can I Use: anchor positioning](https://caniuse.com/css-anchor-positioning) — browser support status

## NuGet / package resolution

- [project.assets.json](https://learn.microsoft.com/en-us/nuget/concepts/dependency-resolution) — NuGet dependency resolution docs
- [ReferenceResolver NuGet package](https://www.nuget.org/packages/ReferenceResolver/) — helper for resolving PackageReference, FrameworkReference
- [Referencing system assemblies in Roslyn](https://luisfsgoncalves.wordpress.com/2017/03/20/referencing-system-assemblies-in-roslyn-compilations/) — practical guide

## File-based apps / compilation portability

- [File-based apps (.NET 10)](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) — single-file C# apps with `#:package` directives, no .csproj needed
- [complog (Jared Parsons)](https://github.com/jaredpar/complog) — create portable compiler log files that can recreate Roslyn `Compilation` instances on any machine

## .NET Interactive / code validation

- [.NET Interactive GitHub](https://github.com/dotnet/interactive) — REPL/notebook engine
- [Creating Interactive .NET Documentation](https://devblogs.microsoft.com/dotnet/creating-interactive-net-documentation/) — Microsoft blog post on the concept

## Related projects

- [BlazorStatic](https://blazorstatic.net/) — .NET static site generator (niche)
- [DocExtractor](https://github.com/YarnSpinnerTool/DocExtractor) — extracts XML doc comments to markdown
