# Snippets compile with GloSharp's own bundled Roslyn, not the user's SDK compiler

The tool ships a pinned Microsoft.CodeAnalysis and targets net8.0 with `RollForward=Major`, so one package runs on any runtime from .NET 8 up. The consequence is that the C# language features a snippet can use are set by the GloSharp release, not by the installed SDK: a new C# feature needs a GloSharp release with a newer Roslyn. The installed SDK only supplies reference packs and restores packages; it never compiles.
