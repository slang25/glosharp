# Shiki and Expressive Code integration research

How glosharp will plug into the rendering layer. Shiki handles syntax highlighting; Expressive Code adds rich presentation features on top.

## Shiki transformers

### What they are

Shiki transformers are hooks that modify the HAST (Hypertext Abstract Syntax Tree) output during code highlighting. They let you inject custom elements (like hover popups) into the rendered code without modifying Shiki's core.

### Transformer lifecycle (execution order)

```
1. preprocess(code, options)       → modify source before tokenization
2. tokens(tokens)                  → modify token array after highlighting
3. span(node, line, col)           → transform individual token <span>s
4. line(node, line)                → transform line <span>s
5. code(node)                      → transform the <code> wrapper
6. pre(node)                       → transform the <pre> wrapper
7. root(root)                      → transform the full HAST tree
8. postprocess(html, options)      → modify final HTML string
```

### Hook signatures

```typescript
interface ShikiTransformer {
  name?: string;
  enforce?: 'pre' | 'post';  // execution priority

  preprocess?(code: string, options: CodeToHastOptions): string | void;
  tokens?(tokens: ThemedToken[][]): void;
  span?(node: Element, line: number, col: number): Element | void;
  line?(node: Element, line: number): Element | void;
  code?(node: Element): Element | void;
  pre?(node: Element): Element | void;
  root?(root: Root): Root | void;
  postprocess?(html: string, options: CodeToHastOptions): string | void;
}
```

### Writing a custom transformer

```typescript
import type { ShikiTransformer } from 'shiki'

const myTransformer: ShikiTransformer = {
  name: 'glosharp',

  // Run glosharp before Shiki tokenizes the code
  preprocess(code, options) {
    if (options.lang !== 'csharp') return;

    // Call glosharp CLI, get JSON
    const result = runGloSharp(code);

    // Store result for later hooks
    this.glosharpResult = result;

    // Return cleaned code (markers removed)
    return result.code;
  },

  // Inject hover popups into the HAST
  root(hast) {
    const result = this.glosharpResult;
    if (!result) return;

    // Walk the tree, find token spans at hover positions,
    // wrap them with popup containers
    for (const hover of result.hovers) {
      injectHoverPopup(hast, hover);
    }

    for (const error of result.errors) {
      injectErrorAnnotation(hast, error);
    }
  },
};
```

### Accessing meta strings

Transformers can read the markdown code fence meta via `this.options.meta`:

````markdown
```csharp glosharp title="example.cs"
var x = 42;
```
````

```typescript
preprocess(code, options) {
  const meta = this.options.meta?.__raw; // "glosharp title=\"example.cs\""
  if (!meta?.includes('glosharp')) return;
  // ...
}
```

### Execution ordering

Use `enforce` to control when your transformer runs relative to others:

```typescript
const transformer: ShikiTransformer = {
  name: 'glosharp',
  enforce: 'pre', // Run before other transformers
  // ...
};
```

## How @shikijs/twoslash works (the pattern to follow)

The twoslash Shiki integration is the template for glosharp:

1. **`preprocess`**: Detect twoslash code blocks, run twoslash, store result, return cleaned code
2. **`root`**: Walk the HAST, match token positions to twoslash data, inject popup elements

The mapping from data to HAST:

| Twoslash data | HAST element |
|---|---|
| Hover | `<span class="twoslash-hover">` wrapping the token + sibling `<div class="twoslash-popup-container">` |
| Error | `<span class="twoslash-error">` with underline + `<div class="twoslash-error-message">` |
| Completion | `<ul class="twoslash-completion-list">` with `<li>` items |

### Renderers

Shiki's twoslash provides pluggable renderers:

- **`rendererRich`** (default): CSS-class-based, supports syntax highlighting in popups
- **`rendererClassic`**: Legacy compatibility format
- **`rendererFloatingVue`**: Vue component templates for VitePress

For glosharp, we'd implement a single renderer initially (rich CSS-class-based).

## Expressive Code plugins

### Plugin structure

```typescript
import { definePlugin } from '@expressive-code/core'

export function pluginGloSharp(options = {}) {
  return definePlugin({
    name: 'glosharp',

    // CSS styles (automatically scoped to EC blocks)
    baseStyles: `
      .glosharp-hover {
        text-decoration: underline dotted;
        cursor: pointer;
      }
      .glosharp-popup {
        display: none;
        position: fixed;
        /* ... */
      }
      .glosharp-hover:hover + .glosharp-popup {
        display: block;
      }
    `,

    // Optional client-side JavaScript
    jsModules: [],

    hooks: {
      preprocessCode: ({ codeBlock }) => { /* ... */ },
      annotateCode: ({ codeBlock }) => { /* ... */ },
      postprocessRenderedBlock: ({ renderData }) => { /* ... */ },
    },
  })
}
```

### Plugin hook lifecycle

EC hooks execute in this order:

**Pre-processing phase:**
1. `preprocessLanguage` — modify language identifier
2. `preprocessMetadata` — process meta string before plugins
3. `preprocessCode` — modify raw code before highlighting

**Analysis phase:**
4. `performSyntaxAnalysis` — Shiki tokenization happens here
5. `postprocessAnalyzedCode` — modify code after tokenization (code becomes read-only after this)

**Annotation phase:**
6. `annotateCode` — add annotations to code ranges (code is read-only)
7. `postprocessAnnotations` — finalize annotations (annotations become read-only after this)

**Rendering phase:**
8. `postprocessRenderedLine` — modify a single line's AST
9. `postprocessRenderedBlock` — modify the entire block's AST
10. `postprocessRenderedBlockGroup` — modify grouped blocks

### Where glosharp hooks in

```
preprocessCode          ← Remove glosharp markers, call CLI, store result
annotateCode            ← Map hovers/errors to EC annotations
postprocessRenderedBlock ← Inject popup HTML elements
```

### Custom annotations

```typescript
import { ExpressiveCodeAnnotation } from '@expressive-code/core'
import { h } from '@expressive-code/core/hast'

class GloSharpHoverAnnotation extends ExpressiveCodeAnnotation {
  constructor(
    readonly hoverText: string,
    readonly parts: GloSharpPart[],
    readonly docs?: string,
    options: { inlineRange: { columnStart: number; columnEnd: number } }
  ) {
    super(options)
  }

  render({ nodesToTransform }) {
    return nodesToTransform.map(node => {
      return h('span.glosharp-hover', [
        node,
        h('div.glosharp-popup', [
          h('code', this.renderParts()),
          this.docs ? h('div.glosharp-docs', this.docs) : null,
        ].filter(Boolean)),
      ])
    })
  }

  private renderParts() {
    return this.parts.map(part =>
      h(`span.glosharp-${part.kind}`, part.text)
    )
  }
}
```

### Adding annotations to code

```typescript
hooks: {
  annotateCode: ({ codeBlock }) => {
    const result = getGloSharpResult(codeBlock);
    if (!result) return;

    for (const hover of result.hovers) {
      const line = codeBlock.getLine(hover.line);
      if (!line) continue;

      line.addAnnotation(new GloSharpHoverAnnotation(
        hover.text,
        hover.parts,
        hover.docs,
        {
          inlineRange: {
            columnStart: hover.character,
            columnEnd: hover.character + hover.length,
          },
        }
      ));
    }
  },
}
```

### Styling with theme awareness

```typescript
export function pluginGloSharp() {
  return definePlugin({
    name: 'glosharp',

    // Define style settings that respond to themes
    styleSettings: {
      popupBackground: { dark: '#1e1e1e', light: '#f3f3f3' },
      popupForeground: { dark: '#d4d4d4', light: '#1f1f1f' },
      popupBorder: { dark: '#454545', light: '#c8c8c8' },
      errorUnderline: { dark: '#f14c4c', light: '#e51400' },
    },

    baseStyles: (context) => `
      .glosharp-popup {
        background: ${context.cssVar('popupBackground')};
        color: ${context.cssVar('popupForeground')};
        border: 1px solid ${context.cssVar('popupBorder')};
      }
      .glosharp-error-underline {
        text-decoration: wavy underline ${context.cssVar('errorUnderline')};
      }
    `,
  })
}
```

### Injecting into rendered output

For elements that can't be expressed as annotations (like popup containers that need specific DOM positioning):

```typescript
hooks: {
  postprocessRenderedBlock: ({ renderData }) => {
    // renderData.blockAst is the full HAST for the code block
    // You can append elements, modify structure, etc.
    const popupContainer = h('div.glosharp-popups', popupElements);
    renderData.blockAst.children.push(popupContainer);
  },
}
```

## CSS anchor positioning for tooltips

Modern CSS provides a native way to position tooltips without JavaScript.

### How it works

```css
/* The token is the anchor */
.glosharp-hover {
  anchor-name: --glosharp-target;
}

/* The popup positions itself relative to the anchor */
.glosharp-popup {
  position: fixed;
  position-anchor: --glosharp-target;
  inset-area: top;        /* position above the anchor */
  margin-bottom: 4px;     /* small gap */
}
```

### Multiple anchors (each hover needs a unique name)

Since we may have many hovers per code block, each needs a unique anchor name. Use inline styles:

```html
<span class="glosharp-hover" style="anchor-name: --th-0">greeting</span>
<div class="glosharp-popup" style="position-anchor: --th-0">
  (local variable) string greeting
</div>

<span class="glosharp-hover" style="anchor-name: --th-1">WriteLine</span>
<div class="glosharp-popup" style="position-anchor: --th-1">
  void Console.WriteLine(string? value)
</div>
```

### Show on hover (pure CSS)

```css
.glosharp-popup {
  display: none;
}

.glosharp-hover:hover + .glosharp-popup,
.glosharp-popup:hover {
  display: block;
}
```

The `:hover` on the popup itself ensures the user can move their mouse to the popup without it disappearing.

### Browser support (as of early 2026)

- Chrome 125+ (May 2024)
- Edge 125+ (May 2024)
- Firefox 147+ (fully shipped)
- Safari 26+ (fully shipped)
- Safari iOS 26+ (fully shipped)

All major browsers now support CSS anchor positioning without flags. This is a solid foundation for glosharp's tooltip positioning strategy.

## How expressive-code-twoslash actually works (reference implementation)

The [`expressive-code-twoslash`](https://github.com/withstudiocms/expressive-code-twoslash) plugin by StudioCMS is the direct pattern for glosharp's EC integration. It's the only real-world example of a twoslash-style EC plugin.

### Plugin structure overview

```typescript
import { definePlugin } from '@expressive-code/core'
import { twoslasher } from '@ec-ts/twoslash'

export default function ecTwoSlash(options = {}) {
  return definePlugin({
    name: 'expressive-code-twoslash',

    // Client-side JS for popup positioning (uses Floating UI, not CSS anchors)
    jsModules: [floatingUiCore, floatingUiDom, popupModule],

    // Theme-aware style settings
    styleSettings: twoSlashStyleSettings,

    // CSS generation function
    baseStyles: (context) => getTwoSlashBaseStyles(context),

    hooks: {
      // Single hook: preprocessCode
      preprocessCode: async ({ codeBlock }) => { /* ... */ }
    }
  })
}
```

### Key insight: uses `preprocessCode`, not `annotateCode`

The plugin does **everything** in the `preprocessCode` hook — not `annotateCode`. This is because it needs to:
1. Run twoslash (which modifies the code — removing markers)
2. Replace the code block's content with the cleaned output
3. Add annotations to the modified lines

By doing it all in `preprocessCode`, the code is ready for Shiki's syntax highlighting in the `performSyntaxAnalysis` phase that follows.

### The preprocessCode flow

```typescript
hooks: {
  preprocessCode: async ({ codeBlock }) => {
    // 1. Check if this block should be processed
    if (!shouldTransform(codeBlock)) return;

    // 2. Run twoslash on the raw code
    const twoslash = twoslasher(codeBlock.code, extension, {
      compilerOptions: { /* ... */ },
      // ...options
    });

    // 3. Replace the code block's text with twoslash output
    //    (markers like ^? are removed, code may be shorter)
    processTwoslashCodeBlock(codeBlock, twoslash.code);

    // 4. Process each node type from twoslash results
    for (const node of twoslash.nodes) {
      const line = codeBlock.getLine(node.line);
      if (!line) continue;

      switch (node.type) {
        case 'hover':
          // Render the type info as syntax-highlighted code
          const codeType = await renderType(node.text, config);
          const docs = renderJSDocs(node.docs, node.tags);
          line.addAnnotation(new TwoslashHoverAnnotation(
            node, codeType, docs
          ));
          break;

        case 'query':
          // Static type display (shown inline, not on hover)
          const queryType = await renderType(node.text, config);
          const queryDocs = renderJSDocs(node.docs, node.tags);
          line.addAnnotation(new TwoslashStaticAnnotation(
            node, line, queryType, queryDocs
          ));
          break;

        case 'error':
          // Underline + error box
          line.addAnnotation(new TwoslashErrorUnderlineAnnotation(node));
          line.addAnnotation(new TwoslashErrorBoxAnnotation(node, line));
          break;

        case 'highlight':
          line.addAnnotation(new TwoslashHighlightAnnotation(node));
          break;

        case 'completion':
          line.addAnnotation(new TwoslashCompletionAnnotation(node));
          break;

        case 'tag':
          line.addAnnotation(new TwoslashCustomTagsAnnotation(node));
          break;
      }
    }
  }
}
```

### Annotation classes in detail

**TwoslashHoverAnnotation** — the most important one:

```typescript
class TwoslashHoverAnnotation extends ExpressiveCodeAnnotation {
  constructor(hover, codeType, renderedDocs) {
    super({
      inlineRange: {
        columnStart: hover.character,
        columnEnd: hover.character + hover.length,
      }
    });
    this.codeType = codeType;      // HAST element with syntax-highlighted type
    this.renderedDocs = renderedDocs; // { docs: Element, tags: Element }
  }

  render({ nodesToTransform }) {
    return nodesToTransform.map(node => {
      // Wrap the token in a hover container with popup
      return h('span.twoslash', [
        h('span.twoslash-hover', [
          h('div.twoslash-popup-container.not-content', [
            h('code.twoslash-popup-code', [this.codeType]),
            this.renderedDocs.docs,
            this.renderedDocs.tags,
          ]),
          node,  // the original token
        ])
      ]);
    });
  }
}
```

The popup is **nested inside** the hover span, not a sibling element. This means CSS `:hover` on the parent reveals the child popup — no anchor positioning needed in their implementation (they use Floating UI JS instead).

**TwoslashStaticAnnotation** — for `^?` query results shown inline:

```typescript
class TwoslashStaticAnnotation extends ExpressiveCodeAnnotation {
  render({ nodesToTransform }) {
    return nodesToTransform.map(node => {
      return h('span.twoslash-noline', [
        node,
        h('div.twoslash-static-container.not-content', [
          h('code', [this.codeType]),
          this.renderedDocs.docs,
          this.renderedDocs.tags,
        ]),
      ]);
    });
  }
}
```

Static annotations render the type info **below the code line**, always visible (not just on hover). The `margin-left` is calculated from the character position to align with the queried token.

**TwoslashErrorUnderlineAnnotation + TwoslashErrorBoxAnnotation**:

Two separate annotations for errors: the underline marks the error span inline, the box displays the error message below the line with severity-based coloring (error=red, warning=yellow, suggestion=green, message=blue).

### How type info is rendered in popups

The plugin doesn't just dump plain text — it **re-renders the type info as syntax-highlighted TypeScript** using a second ExpressiveCode instance:

```typescript
async function renderType(typeText: string, config) {
  // Clean up the type text (remove "property", "method" prefixes, etc.)
  const cleaned = defaultHoverInfoProcessor(typeText);

  // Create a mini EC instance to syntax-highlight the type
  const ec = new ExpressiveCode(ecConfig(config));
  const result = await ec.render({
    code: cleaned,
    language: 'typescript',
  });

  return result.renderedGroupAst;
}
```

This is clever — the popup code is syntax-highlighted using the same theme as the main code block. **For glosharp, we'd do the same but render C# type info.**

### Popup positioning: Floating UI vs CSS anchors

The existing twoslash plugin uses **Floating UI** (JavaScript) for popup positioning — it bundles `floating-ui-core.min.js` and `floating-ui-dom.min.js` as EC `jsModules`. This means:
- Runtime JS is required in the browser
- The popup position is calculated on hover
- Works in all browsers (no anchor positioning needed)

**For glosharp, we have a choice:**
- **CSS anchor positioning** — zero JS, modern browsers all support it now, cleaner markup
- **Floating UI** — the proven approach, works everywhere including older browsers
- **Both** — CSS anchors by default, Floating UI as an opt-in fallback

Given that all major browsers now support CSS anchor positioning (Safari 26+, Chrome 125+, Firefox 147+), the CSS-only approach is viable for glosharp.

### Style settings architecture

The plugin defines theme-aware style settings that derive from EC's theme:

```typescript
const twoSlashStyleSettings = {
  // Popup appearance — derived from the EC theme's editor colors
  popupBorderColor: ({ theme }) => theme.titleBar?.activeBorder || theme.colors['editor.background'],
  popupBackground: ({ theme }) => theme.colors['editor.background'],
  popupTextColor: ({ theme }) => theme.colors['editor.foreground'],

  // Error colors
  errorColor: '#ff5555',
  warningColor: '#ffff55',
  suggestionColor: '#55ff55',
  messageColor: '#5555ff',

  // Highlight colors (LCH color space for perceptual consistency)
  highlightHue: '284',
  highlightDefaultLuminance: { dark: '32%', light: '75%' },
  highlightDefaultChroma: '40',
  // ... etc
};
```

These become CSS variables via EC's `cssVar()` function, automatically adapting to light/dark themes.

### Trigger detection

The plugin processes code blocks based on:
1. **Language**: must be a TypeScript/JavaScript variant
2. **Meta string**: can include `twoslash` to opt-in
3. **Configuration**: can be set to process all TS/JS blocks automatically

### Lessons for glosharp's EC plugin

1. **Use `preprocessCode`, not `annotateCode`** — we need to modify the code (strip markers) before syntax highlighting
2. **Re-render type info with EC** — create a mini EC instance to syntax-highlight C# type signatures in popups
3. **Annotations contain the popup markup** — the `render()` method wraps tokens with popup containers
4. **Use EC's `styleSettings`** — derive popup colors from the theme for automatic dark/light support
5. **Consider CSS anchors over Floating UI** — browser support is now universal, eliminates runtime JS
6. **Two annotations per error** — one for the inline underline, one for the message box below
7. **Static annotations for `^?` queries** — always-visible type display below the line, not just on hover

## Integration architecture summary

```
                    Shiki path                    EC path
                    ──────────                    ───────
                         │                            │
              preprocess hook              preprocessCode hook
              (call glosharp CLI)           (call glosharp CLI)
                         │                            │
              Shiki tokenizes              EC/Shiki tokenizes
                         │                            │
              root hook                    annotateCode hook
              (inject HAST nodes)          (add EC annotations)
                         │                            │
              CSS for popups               postprocessRenderedBlock
              (inline or external)         (inject popup HTML)
                         │                            │
                         ▼                            ▼
                    HTML + CSS                   HTML + CSS
                    with hover popups            with hover popups
```

Both paths consume the same glosharp JSON output. The difference is just how they inject it into the rendering pipeline.

## Open questions

- Should we use `position: absolute` inside a `position: relative` container instead of CSS anchor positioning, for broader compatibility?
- For the EC plugin, should popup HTML go inside the annotation render, or be appended in `postprocessRenderedBlock`?
- How do we handle code blocks with many hovers — will unique anchor names per-hover scale?
- Should the Shiki transformer cache glosharp results between builds (e.g., based on source hash)?
- Can we share CSS between the Shiki and EC integrations, or do they need separate stylesheets?
