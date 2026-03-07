# How twoslash works

Twoslash is a markup system that bridges the TypeScript compiler's type information with syntax-highlighted code rendering. It's the model twohash aims to replicate for C#.

## Core concept

You write TypeScript code with special comment markers. Twoslash runs the code through the TypeScript compiler, extracts type information at the marked positions, and outputs structured metadata alongside the cleaned code. Renderers then use this metadata to display hover tooltips, error annotations, and completions.

## Marker syntax

### `^?` — Hover query

The most important marker. Place on a comment line; the `^` character's column position determines which token on the line above gets queried.

```typescript
const greeting = "Hello";
//        ^?
// Result: const greeting: "Hello"
```

Multiple queries per file:

```typescript
const x = Math.random();
//    ^?
const y = x > 0.5 ? "yes" : "no";
//    ^?
```

### `^|` — Completion query

Triggers IntelliSense/autocomplete at the position:

```typescript
const obj = { name: "John", age: 30 };
obj.
//  ^|
// Result: completions list [name, age, ...]
```

### `// @errors: NNNN` — Expected errors

Declare that specific TypeScript errors are expected (so they don't fail the build):

```typescript
// @errors: 2322
const x: string = 42;
// This would normally fail, but the error is expected
```

Multiple error codes: `// @errors: 2322 2345`

### `// @noErrors` — Assert no errors

The inverse — assert the code compiles cleanly:

```typescript
// @noErrors
const x: number = 42;
```

### Compiler option overrides

```typescript
// @strict
// @noImplicitAny: false
// @target: ES2020
// @module: ESNext
```

These set TypeScript compiler options for the snippet.

### Cut markers

Remove setup code from the visible output:

```typescript
import { something } from './setup';
const config = { /* ... */ };
// ---cut---
// Only code below the cut is shown to readers
const result = something(config);
//      ^?
```

Also: `// ---cut-before---` and `// ---cut-after---` for more control.

### `// @noEmit` — Suppress emission

Prevents code from being emitted in output.

## Internal pipeline

```
1. INPUT
   TypeScript code with markers
        │
2. PARSE MARKERS
   Extract markers, record their types and positions
   Remove marker lines from the code
   Build position offset map (lines removed shift positions)
        │
3. COMPILE
   Create TypeScript Program with cleaned code:
     ts.createProgram(["file.ts"], compilerOptions, compilerHost)
   Get TypeChecker:
     program.getTypeChecker()
   Collect diagnostics:
     program.getSemanticDiagnostics()
        │
4. EXECUTE QUERIES
   For each ^? marker:
     typeChecker.getTypeAtLocation(nodeAtPosition)
     typeChecker.typeToString(type)
     Also: getQuickInfoAtPosition() for docs and tags
   For each ^| marker:
     languageService.getCompletionsAtPosition()
   For // @errors:
     Filter diagnostics to expected error codes
        │
5. MAP POSITIONS
   Convert positions from cleaned code back to output coordinates
   Account for removed marker lines and cut sections
        │
6. OUTPUT
   TwoslashReturn object with code, hovers, errors, completions
```

## TypeScript compiler API calls

The key TS APIs twoslash uses internally:

```typescript
// Create a compiler host (virtual file system)
const compilerHost = ts.createCompilerHost(compilerOptions);

// Create program (entry point to the compiler)
const program = ts.createProgram(
  ["file.ts"],
  compilerOptions,
  compilerHost
);

// Get the type checker (semantic analysis engine)
const typeChecker = program.getTypeChecker();

// Get source file AST
const sourceFile = program.getSourceFile("file.ts");

// Find the node at a position
const node = findNodeAtPosition(sourceFile, position);

// Get type at a location
const type = typeChecker.getTypeAtLocation(node);
const typeString = typeChecker.typeToString(type);

// Get symbol at a location
const symbol = typeChecker.getSymbolAtLocation(node);

// Get quick info (hover text with docs)
const quickInfo = languageService.getQuickInfoAtPosition("file.ts", position);
// quickInfo.displayParts - structured text parts
// quickInfo.documentation - JSDoc comments
// quickInfo.tags - @param, @returns, etc.

// Get completions
const completions = languageService.getCompletionsAtPosition(
  "file.ts", position, {}
);

// Get diagnostics
const diagnostics = program.getSemanticDiagnostics(sourceFile);
// Each diagnostic: { start, length, messageText, code, category }
```

## Output data model

The core output type — this is what renderers consume:

```typescript
interface TwoslashReturn {
  /** The cleaned code with all markers removed */
  code: string;

  /** Hover information at queried positions */
  hovers: Array<{
    line: number;        // 0-based line in cleaned code
    character: number;   // 0-based column
    length: number;      // length of the target token
    text: string;        // display text (e.g., "const x: number")
    docs?: string;       // JSDoc documentation
    tags?: Array<{       // JSDoc tags
      name: string;
      text?: string;
    }>;
  }>;

  /** Compiler diagnostics */
  errors: Array<{
    line: number;
    character: number;
    length: number;
    code: number;         // TypeScript error code (e.g., 2322)
    category: number;     // 0=Warning, 1=Error, 2=Suggestion, 3=Message
    message: string;      // Human-readable error message
  }>;

  /** Completion results */
  completions: Array<{
    line: number;
    character: number;
    completions: Array<{
      name: string;
      kind: string;           // "Property", "Method", "Variable", etc.
      kindModifiers?: string; // "readonly", "deprecated", etc.
    }>;
  }>;

  /** Static quick info (hover data for all identifiers, not just queried) */
  staticQuickInfos?: Array<{
    line: number;
    character: number;
    length: number;
    text: string;
    documentation?: string;
    tags?: Array<{ name: string; text?: string }>;
  }>;
}
```

### Key design choices in the data model

- **Position-based**: Everything is keyed by `line` + `character` in the cleaned code
- **Flat arrays**: No nesting beyond the top level — easy to iterate
- **Text-first**: `text` is always a pre-formatted string; structured parts are in `displayParts` (via quick info)
- **Separate concerns**: Hovers, errors, and completions are independent arrays

## How Shiki consumes twoslash output

The `@shikijs/twoslash` transformer bridges twoslash data into Shiki's HAST (HTML Abstract Syntax Tree).

### Transformer lifecycle

```typescript
import { createTransformerFactory } from '@shikijs/twoslash/core'

const transformer = {
  name: 'twoslash',

  // 1. Run twoslash on the source code before tokenization
  preprocess(code, options) {
    if (!isTwoslashBlock(options)) return code;
    const result = twoslash(code, options.lang);
    this.twoslashResult = result;
    return result.code; // Return cleaned code for syntax highlighting
  },

  // 2-5. span, line, code, pre — mostly passthrough

  // 6. Inject hover/error elements into the final HAST
  root(hast) {
    const result = this.twoslashResult;
    if (!result) return;

    // For each hover, find the token span at that position
    // and wrap it with a hover container + popup element
    for (const hover of result.hovers) {
      const targetSpan = findSpanAtPosition(hast, hover.line, hover.character);
      wrapWithHoverPopup(targetSpan, hover);
    }

    // For each error, add underline decoration and error message
    for (const error of result.errors) {
      const targetSpan = findSpanAtPosition(hast, error.line, error.character);
      addErrorDecoration(targetSpan, error);
    }
  },
};
```

### Rendered HTML structure

```html
<pre class="shiki twoslash">
  <code>
    <!-- Normal token -->
    <span style="color:#CF222E">const</span>

    <!-- Token with hover -->
    <span class="twoslash-hover" data-twoslash-popup-type>
      <span style="color:#1F2328">greeting</span>
      <div class="twoslash-popup-container">
        <code class="twoslash-popup-code">
          const greeting: "Hello"
        </code>
        <div class="twoslash-popup-docs">
          The greeting message.
        </div>
      </div>
    </span>

    <!-- Token with error -->
    <span class="twoslash-error" data-twoslash-error-code="2322">
      <span class="twoslash-error-underline" style="color:#1F2328">42</span>
    </span>
  </code>
</pre>
<!-- Error message below code block -->
<div class="twoslash-error-message">
  Type 'number' is not assignable to type 'string'. [2322]
</div>
```

### Rendering options

Shiki's twoslash integration offers three renderers:

1. **`rendererRich`** (default) — CSS class-based, syntax highlighting in hover popups, `twoslash-` prefixed classes
2. **`rendererClassic`** — Legacy format from the original `shiki-twoslash` package
3. **`rendererFloatingVue`** — Vue component output for VitePress, uses Floating UI for positioning

## Multi-file support

Twoslash can handle multiple files compiled together:

```typescript
// @filename: types.ts
export interface User {
  name: string;
  age: number;
}

// @filename: index.ts
import { User } from './types';

const user: User = { name: "Alice", age: 30 };
//    ^?
```

The `// @filename:` directive switches context. All files are compiled in a single program.

## Position tracking

When markers are removed, line numbers shift. Twoslash maintains an internal mapping:

```
Original (with markers):          Cleaned (output):
Line 0: const x = 42;            Line 0: const x = 42;
Line 1: //    ^?                  Line 1: console.log(x);
Line 2: console.log(x);

Hover result: { line: 0, character: 6, text: "const x: 42" }
// line 0 refers to the *cleaned* code
```

## Lessons for twohash

1. **The marker syntax works** — `^?` and `^|` are intuitive and familiar. Reuse them.
2. **Position mapping is critical** — must track how removed lines shift positions.
3. **Pre-formatted text + structured parts** — provide both for flexibility.
4. **The preprocess hook is the key integration point** — run the compiler before Shiki tokenizes.
5. **Keep the output flat** — arrays of positioned items, not nested structures.
6. **Cut markers are essential** — real code needs setup that readers shouldn't see.
7. **Expected errors are a feature** — documentation often intentionally shows errors.
