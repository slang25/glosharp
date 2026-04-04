# Data format

The JSON output from glosharp. This is the contract between the C# core and all JS integrations.

## Design principles

- Mirror twoslash's structure where it makes sense (familiarity for ecosystem)
- Add C#-specific fields where needed
- Keep it flat and simple — integrations should be easy to write
- Include enough information for rich rendering without requiring a second pass

## Top-level structure

```jsonc
{
  // The processed source code (markers removed)
  "code": "var x = 42;\nConsole.WriteLine(x);",

  // Original source with markers (for debugging)
  "original": "var x = 42;\n//   ^?\nConsole.WriteLine(x);",

  // Language (always "csharp" for now)
  "lang": "csharp",

  // Hover information at queried positions
  "hovers": [ /* ... */ ],

  // Compiler diagnostics
  "errors": [ /* ... */ ],

  // Completion results (if any ^| markers)
  "completions": [ /* ... */ ],

  // Highlighted spans (user-marked regions)
  "highlights": [ /* ... */ ],

  // Lines/regions that were hidden from output
  "hidden": [ /* ... */ ],

  // Metadata about the compilation
  "meta": {
    "targetFramework": "net9.0",
    "packages": [
      { "name": "Newtonsoft.Json", "version": "13.0.3" }
    ],
    "compileSucceeded": true
  }
}
```

## Hover information

Each hover corresponds to a `^?` marker in the source.

```jsonc
{
  "hovers": [
    {
      // Position in the processed code (markers removed)
      "line": 0,
      "character": 4,
      "length": 1,         // length of the target token

      // The display text (what you'd see in VS tooltip)
      "text": "(local variable) int x",

      // Structured parts for rich rendering
      "parts": [
        { "kind": "punctuation", "text": "(" },
        { "kind": "text", "text": "local variable" },
        { "kind": "punctuation", "text": ")" },
        { "kind": "space", "text": " " },
        { "kind": "keyword", "text": "int" },
        { "kind": "space", "text": " " },
        { "kind": "localName", "text": "x" }
      ],

      // XML doc comment (if available)
      "docs": "Gets or sets the value.",

      // Symbol kind for icon rendering
      "symbolKind": "Local",

      // The target token text
      "targetText": "x"
    }
  ]
}
```

### Parts kinds

The `parts` array enables syntax-highlighted hover text (just like VS Code tooltips). Kinds include:

- `keyword` — C# keywords (`int`, `string`, `class`, `async`)
- `className`, `structName`, `interfaceName`, `enumName`, `delegateName` — type names
- `methodName`, `propertyName`, `fieldName`, `eventName` — member names
- `localName`, `parameterName` — variable names
- `namespaceName` — namespace names
- `punctuation` — `(`, `)`, `<`, `>`, `.`, `,`
- `operator` — `?`, `=`
- `space` — whitespace
- `text` — plain descriptive text
- `lineBreak` — newline in multi-line display

These map to Roslyn's `SymbolDisplayPartKind`.

## Compiler diagnostics

```jsonc
{
  "errors": [
    {
      "line": 3,
      "character": 8,
      "length": 5,
      "code": "CS1002",
      "message": "; expected",
      "severity": "error",    // "error" | "warning" | "info" | "hidden"

      // Whether this error was expected (via // @errors marker)
      "expected": true
    }
  ]
}
```

## Completions

For `^|` markers — show what IntelliSense would offer at a position.

```jsonc
{
  "completions": [
    {
      "line": 2,
      "character": 5,
      "items": [
        {
          "label": "WriteLine",
          "kind": "Method",
          "detail": "void Console.WriteLine(string? value)"
        },
        {
          "label": "Write",
          "kind": "Method",
          "detail": "void Console.Write(string? value)"
        }
      ]
    }
  ]
}
```

## Highlights

For user-marked highlighted spans (similar to Shiki's line highlighting but position-based).

```jsonc
{
  "highlights": [
    {
      "line": 2,
      "character": 0,
      "length": 25,
      "kind": "highlight"    // "highlight" | "add" | "remove" | "focus"
    }
  ]
}
```

## Example: full input/output

### Input

```csharp
var greeting = "Hello, World!";
//      ^?
Console.WriteLine(greeting);
//                  ^?
```

### Output

```json
{
  "code": "var greeting = \"Hello, World!\";\nConsole.WriteLine(greeting);",
  "original": "var greeting = \"Hello, World!\";\n//      ^?\nConsole.WriteLine(greeting);\n//                  ^?",
  "lang": "csharp",
  "hovers": [
    {
      "line": 0,
      "character": 4,
      "length": 8,
      "text": "(local variable) string greeting",
      "parts": [
        { "kind": "punctuation", "text": "(" },
        { "kind": "text", "text": "local variable" },
        { "kind": "punctuation", "text": ")" },
        { "kind": "space", "text": " " },
        { "kind": "keyword", "text": "string" },
        { "kind": "space", "text": " " },
        { "kind": "localName", "text": "greeting" }
      ],
      "docs": null,
      "symbolKind": "Local",
      "targetText": "greeting"
    },
    {
      "line": 1,
      "character": 18,
      "length": 8,
      "text": "(local variable) string greeting",
      "parts": [
        { "kind": "punctuation", "text": "(" },
        { "kind": "text", "text": "local variable" },
        { "kind": "punctuation", "text": ")" },
        { "kind": "space", "text": " " },
        { "kind": "keyword", "text": "string" },
        { "kind": "space", "text": " " },
        { "kind": "localName", "text": "greeting" }
      ],
      "docs": null,
      "symbolKind": "Local",
      "targetText": "greeting"
    }
  ],
  "errors": [],
  "completions": [],
  "highlights": [],
  "hidden": [],
  "meta": {
    "targetFramework": "net9.0",
    "packages": [],
    "compileSucceeded": true
  }
}
```

## C#-specific considerations

### Overloads

Methods with multiple overloads should show the overload count:

```jsonc
{
  "text": "void Console.WriteLine(string? value) (+ 17 overloads)",
  "overloadCount": 18
}
```

### Nullable annotations

The nullable context affects display. `string` vs `string?` should be accurate:

```jsonc
{
  "text": "(parameter) string? value"
}
```

### Generic types

Full generic display:

```jsonc
{
  "text": "System.Collections.Generic.List<string>"
}
```

### Extension methods

Show the extending type:

```jsonc
{
  "text": "(extension) IEnumerable<TResult> Enumerable.Select<TSource, TResult>(this IEnumerable<TSource> source, Func<TSource, TResult> selector)"
}
```

## Open questions

- Should `parts` be optional (only included when requested) to keep output smaller?
- Should we include a `range` object (`{ start: { line, character }, end: { line, character } }`) in addition to flat line/character/length?
- How should we handle multi-line hover text (e.g., XML doc comments with paragraphs)?
- Should completions include full signature info or just labels?
