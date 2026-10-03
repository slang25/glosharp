# `glosharp render` highlights with Roslyn's classifier and its own theme records, not TextMate/Shiki

The .NET renderer already holds a compilation, so it uses Roslyn's semantic classifier and maps classification names to colours via small C# theme records (github-dark, github-light), emitting self-contained HTML with the theme inlined. This avoids bundling a TextMate engine and grammars into the .NET tool and gives more accurate, semantic highlighting.

## Consequences

- Colours won't exactly match Shiki/Expressive Code output (Roslyn classifications don't map 1:1 to TextMate scopes), and VS Code themes can't be imported.
- Because colours are inlined, each theme is a separate render; GitBook publishes one artifact directory per theme.
