# Glo#

**twoslash for C#** — extract rich symbol metadata from C# code using Roslyn, for rendering beautiful code snippets in docs, blogs, and slides.

## The problem

TypeScript has [twoslash](https://twoslash.netlify.app/), which extracts compiler-derived type information and renders it inline in code snippets. C# has nothing equivalent. Documentation authors either manually annotate code or ship plain syntax-highlighted blocks with no type information.

## The vision

Run `glosharp-cli` against C# code and get:
- **Hover information** — type signatures, XML doc comments, just like VS/VS Code tooltips
- **Compile verification** — fail in CI if the code doesn't compile
- **Structured metadata** — JSON output that integrations can consume
- **Beautiful rendering** — via Expressive Code plugin, Shiki transformer, or standalone HTML/CSS

## Research questions

1. How does twoslash work internally? What's the data model? → [01-twoslash-architecture](research/01-twoslash-architecture.md)
2. What Roslyn APIs do we need for symbol metadata extraction? → [02-roslyn-metadata-extraction](research/02-roslyn-metadata-extraction.md)
3. How do Shiki transformers and Expressive Code plugins work? → [03-shiki-and-expressive-code](research/03-shiki-and-expressive-code.md)
4. How do we resolve NuGet packages for compilation? → [04-nuget-resolution](research/04-nuget-resolution.md)
5. How do people manage code snippets in docs today? → [05-code-snippet-management](research/05-code-snippet-management.md)
6. What doc/blog frameworks should we target? → [06-doc-framework-landscape](research/06-doc-framework-landscape.md)

## Design

- [Architecture](design/architecture.md) — system components and data flow
- [Data format](design/data-format.md) — output metadata JSON schema
- [Integration points](design/integration-points.md) — Shiki, Expressive Code, standalone renderer
- [Decisions](design/decisions.md) — key decisions log

## References

- [Annotated links](references/links.md)
