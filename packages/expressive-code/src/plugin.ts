import { createTwohash, type TwohashOptions, type TwohashResult, type TwohashHover, type TwohashError, type TwohashDisplayPart } from 'twohash'

export interface PluginTwohashOptions extends TwohashOptions {}

const TWOHASH_MARKER_REGEX = /\/\/\s*\^[?|]|\/\/\s*@errors:|\/\/\s*@noErrors|\/\/\s*---cut---|\/\/\s*@hide|\/\/\s*@show/

function hasMarkers(code: string): boolean {
  return TWOHASH_MARKER_REGEX.test(code)
}

// Style settings for theme-aware colors
const styleSettings = {
  popupBackground: { dark: '#1e1e1e', light: '#f3f3f3' },
  popupForeground: { dark: '#d4d4d4', light: '#1e1e1e' },
  popupBorder: { dark: '#3c3c3c', light: '#c8c8c8' },
  errorUnderline: { dark: '#f44747', light: '#e51400' },
  errorBackground: { dark: 'rgba(244, 71, 71, 0.1)', light: 'rgba(229, 20, 0, 0.1)' },
}

// Part kind colors (VS Code-like)
const partColors: Record<string, { dark: string; light: string }> = {
  keyword: { dark: '#569cd6', light: '#0000ff' },
  className: { dark: '#4ec9b0', light: '#267f99' },
  structName: { dark: '#4ec9b0', light: '#267f99' },
  interfaceName: { dark: '#b8d7a3', light: '#267f99' },
  enumName: { dark: '#b8d7a3', light: '#267f99' },
  delegateName: { dark: '#4ec9b0', light: '#267f99' },
  typeParameterName: { dark: '#b8d7a3', light: '#267f99' },
  methodName: { dark: '#dcdcaa', light: '#795e26' },
  propertyName: { dark: '#9cdcfe', light: '#001080' },
  fieldName: { dark: '#9cdcfe', light: '#001080' },
  eventName: { dark: '#9cdcfe', light: '#001080' },
  localName: { dark: '#9cdcfe', light: '#001080' },
  parameterName: { dark: '#9cdcfe', light: '#001080' },
  namespaceName: { dark: '#d4d4d4', light: '#1e1e1e' },
  punctuation: { dark: '#d4d4d4', light: '#1e1e1e' },
  operator: { dark: '#d4d4d4', light: '#1e1e1e' },
  text: { dark: '#d4d4d4', light: '#1e1e1e' },
}

function buildBaseStyles(): string {
  const partColorRules = Object.entries(partColors)
    .map(([kind, colors]) =>
      `.twohash-${kind} { color: var(--twohash-${kind}-dark, ${colors.dark}); }
[data-theme="light"] .twohash-${kind} { color: var(--twohash-${kind}-light, ${colors.light}); }`
    )
    .join('\n')

  return `
.twohash-hover {
  position: relative;
  border-bottom: 1px dotted currentColor;
  cursor: pointer;
}

.twohash-popup {
  display: none;
  position: fixed;
  inset-area: top;
  margin-bottom: 4px;
  z-index: 100;
  max-width: 500px;
  padding: 8px 12px;
  border: 1px solid var(--twohash-popup-border, ${styleSettings.popupBorder.dark});
  border-radius: 4px;
  background: var(--twohash-popup-bg, ${styleSettings.popupBackground.dark});
  color: var(--twohash-popup-fg, ${styleSettings.popupForeground.dark});
  font-size: 0.875em;
  line-height: 1.5;
  white-space: pre-wrap;
  box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
}

.twohash-hover:hover + .twohash-popup,
.twohash-popup:hover {
  display: block;
}

.twohash-popup-code {
  font-family: inherit;
}

.twohash-popup-docs {
  margin-top: 6px;
  padding-top: 6px;
  border-top: 1px solid var(--twohash-popup-border, ${styleSettings.popupBorder.dark});
  font-style: italic;
}

.twohash-error-underline {
  border-bottom: 2px wavy var(--twohash-error-underline, ${styleSettings.errorUnderline.dark});
}

.twohash-error-message {
  display: block;
  padding: 2px 8px;
  margin-top: 2px;
  background: var(--twohash-error-bg, ${styleSettings.errorBackground.dark});
  border-left: 3px solid var(--twohash-error-underline, ${styleSettings.errorUnderline.dark});
  color: var(--twohash-error-underline, ${styleSettings.errorUnderline.dark});
  font-size: 0.85em;
}

.twohash-error-code {
  font-weight: bold;
}

${partColorRules}
`
}

export function pluginTwohash(options: PluginTwohashOptions = {}) {
  const twohash = createTwohash(options)
  const resultCache = new Map<string, TwohashResult>()

  return {
    name: 'twohash',
    baseStyles: buildBaseStyles(),
    styleSettings,

    hooks: {
      async preprocessCode({ codeBlock }: { codeBlock: { code: string; language: string; meta: string } }) {
        const lang = codeBlock.language
        if (lang !== 'csharp' && lang !== 'cs' && lang !== 'c#') return
        if (!hasMarkers(codeBlock.code)) return

        try {
          const result = await twohash.process({ code: codeBlock.code })
          resultCache.set(codeBlock.code, result)
          // Replace code with cleaned version (markers removed)
          codeBlock.code = result.code
        } catch {
          // Silently pass through if CLI fails
        }
      },

      annotateCode({ codeBlock }: { codeBlock: { code: string; getLines: () => Array<{ addAnnotation: (ann: unknown) => void }> } }) {
        // Find result by checking if we have a cached result
        let result: TwohashResult | undefined
        for (const [, r] of resultCache) {
          if (r.code === codeBlock.code) {
            result = r
            break
          }
        }
        if (!result) return

        const lines = codeBlock.getLines()

        // Add hover annotations
        for (const hover of result.hovers) {
          const line = lines[hover.line]
          if (!line) continue
          line.addAnnotation(new TwohashHoverAnnotation(hover))
        }

        // Add error annotations
        for (const error of result.errors) {
          if (error.expected) continue
          const line = lines[error.line]
          if (!line) continue
          line.addAnnotation(new TwohashErrorAnnotation(error))
        }
      },

      postprocessRenderedBlock({ renderData }: { renderData: { blockAst: unknown } }) {
        // The annotations handle rendering via their render() methods
        // Additional post-processing can be added here if needed
      },
    },
  }
}

// Annotation classes
class TwohashHoverAnnotation {
  readonly hover: TwohashHover
  readonly inlineRange: { columnStart: number; columnEnd: number }

  constructor(hover: TwohashHover) {
    this.hover = hover
    this.inlineRange = {
      columnStart: hover.character,
      columnEnd: hover.character + hover.length,
    }
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    const anchorName = `--th-${Math.random().toString(36).slice(2, 8)}`

    const partNodes: HastNode[] = this.hover.parts.map((part: TwohashDisplayPart) => ({
      type: 'element' as const,
      tagName: 'span',
      properties: { class: `twohash-${part.kind}` },
      children: [{ type: 'text' as const, value: part.text }],
    }))

    const popupChildren: HastNode[] = [{
      type: 'element',
      tagName: 'code',
      properties: { class: 'twohash-popup-code' },
      children: partNodes,
    }]

    if (this.hover.docs) {
      popupChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-docs' },
        children: [{ type: 'text', value: this.hover.docs }],
      })
    }

    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'span',
      properties: {
        class: 'twohash-hover',
        style: `anchor-name: ${anchorName}`,
      },
      children: [
        node,
        {
          type: 'element',
          tagName: 'div',
          properties: {
            class: 'twohash-popup',
            style: `position-anchor: ${anchorName}`,
          },
          children: popupChildren,
        },
      ],
    }))
  }
}

class TwohashErrorAnnotation {
  readonly error: TwohashError
  readonly inlineRange: { columnStart: number; columnEnd: number }

  constructor(error: TwohashError) {
    this.error = error
    this.inlineRange = {
      columnStart: error.character,
      columnEnd: error.character + error.length,
    }
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'span',
      properties: { class: 'twohash-error-underline' },
      children: [
        node,
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'twohash-error-message' },
          children: [
            {
              type: 'element',
              tagName: 'span',
              properties: { class: 'twohash-error-code' },
              children: [{ type: 'text', value: this.error.code }],
            },
            { type: 'text', value: `: ${this.error.message}` },
          ],
        },
      ],
    }))
  }
}

interface HastNode {
  type: string
  tagName?: string
  properties?: Record<string, unknown>
  children?: HastNode[]
  value?: string
}
