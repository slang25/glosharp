# Integration points

How glosharp connects to rendering frameworks.

## Marker syntax

GloSharp uses comment-based markers in C# source code, inspired by twoslash.

### Query markers (from twoslash)

```csharp
var x = 42;
//  ^?          // Query hover info for the token above the ^

Console.
//      ^|      // Query completions at this position
```

The `^?` and `^|` markers are placed on a comment line. The `^` aligns with the character position in the line above.

### Directive markers

```csharp
// @errors: CS1002 CS0246     // These compiler errors are expected (don't fail)
// @noErrors                   // Assert no compiler errors
// @highlight: 3-5             // Highlight lines 3 through 5
// @focus: 7                   // Focus on line 7
// @diff: +                    // Mark as added (diff view)
// @diff: -                    // Mark as removed (diff view)
```

### C#-specific markers

```csharp
// @nullable: enable                  // Nullable context
// @using: System.Text.Json           // Add using (hidden from output)
// ---cut-start---                    // Hide a block from output
// ---cut-end---
// ---cut-after---                    // Hide everything below from output
```

### Package and framework directives (via file-based apps)

For .NET 10+, use the SDK's native `#:` directive syntax instead of custom markers:

```csharp
#:package Newtonsoft.Json@13.0.3     // NuGet package (SDK resolves it)
#:sdk Microsoft.NET.Sdk.Web          // Use ASP.NET SDK
#:property TargetFramework=net10.0   // Target framework
```

These `#:` lines are stripped from the rendered output automatically. The file is simultaneously a valid `dotnet build` input and a glosharp snippet. For pre-.NET 10, use a `.csproj` alongside the snippet.

### Cut markers

```csharp
// ---cut-before---    // Remove everything above this line from output
// ---cut-after---     // Remove everything below this line from output
```

These let you include setup code (using directives, class wrappers) that Roslyn needs but the reader doesn't need to see.

## Shiki transformer integration

The Shiki transformer follows the pattern established by `@shikijs/twoslash`.

### How it works

1. **Pre-process**: Extract glosharp markers from the code, call CLI, get JSON
2. **Tokens phase**: No changes (Shiki handles syntax highlighting)
3. **Code phase**: Inject hover popups and error annotations into the HAST

### HAST output structure

For a hover popup:

```html
<span class="glosharp-hover" style="anchor-name: --glosharp-0">x</span>
<div class="glosharp-popup" style="position-anchor: --glosharp-0">
  <code>(<span class="glosharp-text">local variable</span>) <span class="glosharp-keyword">int</span> <span class="glosharp-local">x</span></code>
</div>
```

For an error annotation:

```html
<span class="glosharp-error" data-error-code="CS1002">
  <span class="glosharp-error-underline">foo</span>
</span>
<div class="glosharp-error-message">; expected [CS1002]</div>
```

### CSS anchor positioning for tooltips

```css
.glosharp-popup {
  display: none;
  position: fixed;
  position-anchor: var(--anchor);
  inset-area: top;
  margin-bottom: 4px;

  background: var(--glosharp-popup-bg, #1e1e1e);
  color: var(--glosharp-popup-fg, #d4d4d4);
  border: 1px solid var(--glosharp-popup-border, #454545);
  border-radius: 3px;
  padding: 4px 8px;
  font-size: 0.85em;
  white-space: pre;
  z-index: 10;
}

.glosharp-hover:hover + .glosharp-popup,
.glosharp-popup:hover {
  display: block;
}
```

No JavaScript required. The popup positions itself relative to the anchor element using CSS.

### Fallback for older browsers

```css
/* Fallback: position relative to parent */
@supports not (anchor-name: --x) {
  .glosharp-hover {
    position: relative;
  }
  .glosharp-popup {
    position: absolute;
    bottom: 100%;
    left: 0;
  }
}
```

## Expressive Code plugin integration

### Plugin structure

```typescript
import { definePlugin } from '@expressive-code/core'

export const pluginGloSharp = (options = {}) => {
  return definePlugin({
    name: 'glosharp',

    // Inject CSS for hover popups
    baseStyles: () => glosharpStyles,

    // Optional client-side JS (if needed beyond CSS)
    jsModules: [],

    hooks: {
      // Process code blocks marked with glosharp
      preprocessCode: ({ codeBlock }) => {
        // Detect if this block should be processed
        // (e.g., has glosharp markers or meta flag)
      },

      annotateCode: ({ codeBlock }) => {
        // Call glosharp CLI, get JSON
        // Map hovers/errors to EC annotations
        // Add InlineStyleAnnotation or custom annotations
      },

      postprocessRenderedBlock: ({ renderData }) => {
        // Inject popup HTML elements after rendering
      },
    },
  })
}
```

### EC annotation mapping

GloSharp metadata maps to EC concepts:

| GloSharp data | EC concept |
|---|---|
| Hover info | Custom `GloSharpHoverAnnotation` with popup rendering |
| Error underline | `InlineStyleAnnotation` with error decoration |
| Error message | Custom `GloSharpErrorAnnotation` rendered below the line |
| Line highlight | `InlineStyleAnnotation` with highlight background |
| Diff markers | Built-in EC diff support |

### Custom annotation class

```typescript
class GloSharpHoverAnnotation extends ExpressiveCodeAnnotation {
  hoverText: string
  parts: GloSharpPart[]
  docs?: string

  render({ nodesToTransform }) {
    // Wrap the target token in a hover container
    // Add a popup element with formatted type info
    return nodesToTransform.map(node => ({
      type: 'element',
      tagName: 'span',
      properties: { class: 'glosharp-hover' },
      children: [
        node,
        this.renderPopup(),
      ],
    }))
  }
}
```

## Standalone renderer

For environments without Shiki or EC (Hugo, Jekyll, plain HTML).

### How it works

The CLI itself can produce complete HTML:

```bash
glosharp render src/Example.cs --theme github-dark --format html
```

This:
1. Runs Roslyn analysis (same as `process`)
2. Applies TextMate-compatible syntax highlighting (possibly via Roslyn's classifier)
3. Generates self-contained HTML with inline `<style>` block
4. Includes CSS anchor positioning for hover popups

### Output

```html
<div class="glosharp-code" data-theme="github-dark">
  <style>/* inline theme + popup styles */</style>
  <pre><code>
    <span class="mtk6">var</span> <span class="glosharp-hover" style="anchor-name: --th-0"><span class="mtk1">greeting</span></span> = <span class="mtk4">"Hello!"</span>;
  </code></pre>
  <div class="glosharp-popup" style="position-anchor: --th-0">
    <code>(local variable) string greeting</code>
  </div>
</div>
```

### Alternative: JSON + user's own renderer

Some users may want to consume the JSON and build their own rendering. The `process` command outputs JSON, giving full control.

## Open questions

- Should the Shiki transformer run the CLI on every build, or support a pre-processed JSON cache?
- For the standalone renderer, should we bundle a syntax highlighter (like tree-sitter) in the .NET tool, or require the user to handle highlighting?
- Should we support a "dry run" mode that shows what hovers would be extracted without full Roslyn compilation?
- How do we handle dark/light theme switching in the popup styles?
