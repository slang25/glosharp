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

  // Compiler diagnostics in the visible code (plus Glo# GS000x diagnostics)
  "errors": [ /* ... */ ],

  // Error-severity diagnostics located in hidden (cut/region) code — same shape as errors,
  // with "line": -1. Always present.
  "hiddenErrors": [ /* ... */ ],

  // Completion results (if any ^| markers)
  "completions": [ /* ... */ ],

  // Highlighted spans (user-marked regions)
  "highlights": [ /* ... */ ],

  // Runs of input lines hidden from output (cut markers, --region)
  "hidden": [
    // "line": the processed line the hidden block sits before (= line count when at the end)
    // "sourceStartLine"/"sourceEndLine": inclusive, 0-based lines in the original input
    { "line": 0, "sourceStartLine": 0, "sourceEndLine": 3 }
  ],

  // Metadata about the compilation
  "meta": {
    "targetFramework": "net9.0",
    "packages": [
      { "name": "Newtonsoft.Json", "version": "13.0.3" }
    ],
    // false if any unexpected error occurred — in visible code, in hidden code
    // (hiddenErrors) or as GS0003 (an @errors expectation that did not fire)
    "compileSucceeded": true,
    "sdk": null,               // from #:sdk
    "langVersion": "latest",   // effective value: marker > config > complog > default
    "nullable": "enable",      // effective value: marker > config > complog > default
    "complog": null,           // the --complog path, when used
    // Non-fatal problems the author should know about. Always present.
    "warnings": [
      "Line 4: the ^? marker points at column 28, past the end of line 3; the hover was skipped."
    ]
  }
}
```

### Positions

All `line` values are 0-based lines of `code` (the processed output). All `character`
values are 0-based **UTF-16 code-unit offsets** within the line: a tab counts as one column,
and a character outside the BMP (e.g. most emoji) counts as two. Carets (`^?`, `^|`) are
interpreted the same way, so align them using the same characters as the target line
(e.g. tabs under tabs).

Lines are split on `\n` only; a trailing `\r` (CRLF input) stays part of its line.

`errors[].sourceLine` / `errors[].sourceCharacter` (also on `hiddenErrors`) give the position in
the **original input text**, before `#:` directives, marker lines, cut sections and `--region`
filtering were removed. Use them for `file(line,col)` reporting.

## Hover information

Every identifier-like token in the visible code gets a hover (`persistent: false`); each `^?`
marker additionally produces a `persistent: true` hover. Only tokens that carry their own symbol
are hovered — identifiers (including `var`), predefined type keywords, `this`/`base` and
`new()`/anonymous `new`; operators and punctuation never are. A `^?` that points past the end of
its target line, at an empty line, or at a token without a symbol is skipped with a
`meta.warnings` entry. LINQ range variables display as `(range variable) T name`.

Doc comment text resolves `<see langword>`, `<see cref>` (types as `List<T>`, members as
`Type.Member`), nested elements inside `<para>`/`<list>`, and `<inheritdoc/>`.

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
      "docs": { "summary": "Gets or sets the value.", "params": [], "returns": null },

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
      "expected": true,

      // 0-based position in the original input text (see Positions)
      "sourceLine": 5,
      "sourceCharacter": 8
    }
  ]
}
```

### Expected errors, suppression and verification

- `// @errors: CS0029, CS1503` (commas and/or whitespace: `// @errors: CS0029 CS1503`) declares
  the errors expected on the **next code line only**. Matching diagnostics get `expected: true`.
- An expected code that is not reported on that line produces a `GS0003` error at the target
  line and makes `compileSucceeded` false — the snippet no longer shows the error it documents.
- `// @noErrors` / `// @suppressErrors` hide **all** diagnostics (errors, warnings and info),
  including hidden-code errors and GS0003, as in twoslash. `// @suppressErrors: CS0168, CS0219`
  hides only the listed codes.
- Errors in hidden code are reported in `hiddenErrors` (not `errors`, since renderers cannot place
  them) and fail the snippet unless suppressed or expected. Warnings in hidden code are dropped.

### Glo# diagnostic codes

Diagnostics produced by Glo# itself (not the compiler):

| Code | Meaning |
|---|---|
| `GS0001` | Invalid `@langVersion` marker or `langVersion` config value. |
| `GS0002` | Invalid `@nullable` marker or `nullable` config value. |
| `GS0003` | An `// @errors:` expectation whose diagnostic was not reported on its target line. |
| `GS1001` | (`verify` output only) A file could not be processed at all. |
| `GS1002` | (`verify` output only) A snippet failed with no reportable error location. |
| `GS1003` | (`verify` output only, warning) An entry from `meta.warnings`. |

(`GS0001`/`GS0002` were called `TH0001`/`TH0002` before the rename.)

## Completions

For `^|` markers — show what IntelliSense would offer at a position. Items are filtered by the
identifier already typed before the caret (case-insensitive prefix match, like an editor) and
deduplicated by label (overloads appear once). A caret past the end of its line is skipped, and
an empty result adds a `meta.warnings` entry.

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
  "hiddenErrors": [],
  "completions": [],
  "highlights": [],
  "tags": [],
  "hidden": [],
  "meta": {
    "targetFramework": "net9.0",
    "packages": [],
    "compileSucceeded": true,
    "langVersion": "latest",
    "nullable": "enable",
    "warnings": []
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
