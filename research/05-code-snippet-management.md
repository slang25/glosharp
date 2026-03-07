# How people manage code snippets in docs

## The core problem

Code in documentation rots. APIs change, method signatures evolve, and the snippet in the docs silently becomes wrong. There are two fundamental approaches: **duplicate and maintain** or **reference and extract**.

## Approach 1: Inline code in markdown

The simplest approach — write code directly in markdown fences.

```markdown
​```csharp
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
app.MapGet("/", () => "Hello World!");
app.Run();
​```
```

**Pros**: Simple, self-contained, easy to write.
**Cons**: No compilation check, drifts silently, no IDE support while editing.

This is what most blog posts do today.

## Approach 2: Import from external files

Modern doc frameworks support importing code from standalone files that can be compiled and tested independently.

### VitePress

```markdown
<<< @/snippets/hello-world.cs
<<< @/snippets/hello-world.cs{2-5}        <!-- line range -->
<<< @/snippets/hello-world.cs#region-name  <!-- VS Code region -->
```

### Docusaurus

Uses MDX imports:

```mdx
import CodeBlock from '@theme/CodeBlock';
import MyCode from '!!raw-loader!./my-code.cs';

<CodeBlock language="csharp">{MyCode}</CodeBlock>
```

### Astro/Starlight

Uses MDX component imports or Expressive Code's file import syntax.

### Docfx (C#-specific)

Microsoft's Docfx has first-class support for C# code inclusion:

```markdown
[!code-csharp[](Program.cs)]
[!code-csharp[](Program.cs#snippet_name)]
```

It uses C#'s `#region` / `#endregion` markers to extract named snippets:

```csharp
// <snippet_name>
var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();
// </snippet_name>
```

**Pros**: Code lives in a compilable project, IDE support, can run tests against it.
**Cons**: Indirection (harder to read docs source), region markers are noise in the code.

## Approach 3: The "sample project" pattern

Maintain a full compilable project alongside documentation. Snippets are extracted by reference.

```
docs/
  getting-started.md
samples/
  GettingStarted/
    GettingStarted.csproj
    Program.cs
```

The doc references a region in the sample project. CI builds the sample project to verify it compiles. This is the pattern Microsoft uses for learn.microsoft.com.

**Pros**: Guaranteed to compile, can also run tests, full IDE support.
**Cons**: Requires build infrastructure, region markers, extra project maintenance.

## Approach 4: Literate-style / doctest

Extract code blocks from documentation and execute them as tests. Python's `doctest` and Sphinx's `doctest` extension do this. Rust's `rustdoc` tests code blocks by default.

**C# equivalent**: `dotnet try` (now archived) attempted this — extract code from markdown, compile and run it. The concept was right but the tool didn't get ongoing investment.

## How twohash changes the equation

Twohash sits in a unique position: it **requires** compilable code by design (Roslyn needs it). This means:

1. **Code must be valid** — if it doesn't compile, twohash fails, CI fails
2. **Metadata is real** — hover info comes from the compiler, not manual annotation
3. **The sample project pattern is natural** — you need a .csproj for NuGet resolution anyway

### Recommended pattern for twohash

```
docs/
  getting-started.md          # references snippets by file + region
samples/
  GettingStarted/
    GettingStarted.csproj     # real project, real NuGet refs
    Program.cs                # code with #region markers
```

The doc build:
1. `dotnet build samples/` — verify everything compiles
2. `twohash process samples/GettingStarted/Program.cs#region_name` — extract metadata
3. Shiki/EC transformer consumes metadata → rendered HTML

This gives you: compilation verification, real type information, and beautiful rendering — all from a single source of truth.

## Open questions

- Should twohash support inline code blocks (without a .csproj) for simple snippets? Perhaps with default framework references?
- Should we support a special comment syntax to specify NuGet packages inline, like `// @nuget: Newtonsoft.Json@13.0.3`?
- How do we handle snippets that intentionally don't compile (e.g., showing an error)?
