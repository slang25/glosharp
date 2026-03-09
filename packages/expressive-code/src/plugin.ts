import { createTwohash, type TwohashOptions, type TwohashResult, type TwohashHover, type TwohashError, type TwohashDisplayPart, type TwohashCompletion, type TwohashDocComment, type TwohashDocParam, type TwohashDocException } from 'twohash'

export interface PluginTwohashOptions extends TwohashOptions {
  project?: string
  region?: string
}

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
  position-area: top;
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
}

.twohash-popup-summary {
  font-style: italic;
}

.twohash-popup-params,
.twohash-popup-returns,
.twohash-popup-remarks,
.twohash-popup-example,
.twohash-popup-exceptions {
  margin-top: 4px;
  padding-top: 4px;
  border-top: 1px solid var(--twohash-popup-border, ${styleSettings.popupBorder.dark});
}

.twohash-popup-section-label {
  font-size: 0.8em;
  opacity: 0.7;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 2px;
}

.twohash-popup-param {
  display: flex;
  gap: 6px;
  margin: 1px 0;
}

.twohash-popup-param-name {
  font-family: inherit;
  font-weight: bold;
  white-space: nowrap;
}

.twohash-popup-exception {
  display: flex;
  gap: 6px;
  margin: 1px 0;
}

.twohash-popup-exception-type {
  font-family: inherit;
  font-weight: bold;
  white-space: nowrap;
}

.twohash-popup-example pre {
  margin: 2px 0;
  padding: 4px 6px;
  background: rgba(128, 128, 128, 0.1);
  border-radius: 2px;
  font-size: 0.9em;
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

.twohash-completion-list {
  list-style: none;
  margin: 4px 0 0 0;
  padding: 4px 0;
  border: 1px solid var(--twohash-popup-border, ${styleSettings.popupBorder.dark});
  border-radius: 4px;
  background: var(--twohash-popup-bg, ${styleSettings.popupBackground.dark});
  font-size: 0.875em;
  max-height: 200px;
  overflow-y: auto;
}

.twohash-completion-item {
  display: flex;
  gap: 8px;
  padding: 2px 8px;
  align-items: center;
}

.twohash-completion-kind {
  font-size: 0.75em;
  opacity: 0.7;
  min-width: 60px;
}

.twohash-completion-label {
  color: var(--twohash-popup-fg, ${styleSettings.popupForeground.dark});
}

.twohash-completion-detail {
  opacity: 0.6;
  font-size: 0.85em;
  margin-left: auto;
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
          const result = await twohash.process({ code: codeBlock.code, project: options.project, region: options.region })
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

        // Add completion annotations
        for (const completion of result.completions) {
          const line = lines[completion.line]
          if (!line) continue
          line.addAnnotation(new TwohashCompletionAnnotation(completion))
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
      popupChildren.push(...this.renderDocs(this.hover.docs))
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

  private renderDocs(docs: TwohashDocComment): HastNode[] {
    const sections: HastNode[] = []

    // Wrapper div for all docs
    const docsChildren: HastNode[] = []

    if (docs.summary) {
      docsChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-summary' },
        children: [{ type: 'text', value: docs.summary }],
      })
    }

    if (docs.params && docs.params.length > 0) {
      const paramItems: HastNode[] = docs.params.map((p: TwohashDocParam) => ({
        type: 'element' as const,
        tagName: 'div',
        properties: { class: 'twohash-popup-param' },
        children: [
          {
            type: 'element' as const,
            tagName: 'span',
            properties: { class: 'twohash-popup-param-name' },
            children: [{ type: 'text' as const, value: p.name }],
          },
          { type: 'text' as const, value: ` — ${p.text}` },
        ],
      }))

      docsChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-params' },
        children: [
          {
            type: 'element',
            tagName: 'div',
            properties: { class: 'twohash-popup-section-label' },
            children: [{ type: 'text', value: 'Parameters' }],
          },
          ...paramItems,
        ],
      })
    }

    if (docs.returns) {
      docsChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-returns' },
        children: [
          {
            type: 'element',
            tagName: 'div',
            properties: { class: 'twohash-popup-section-label' },
            children: [{ type: 'text', value: 'Returns' }],
          },
          { type: 'text', value: docs.returns },
        ],
      })
    }

    if (docs.remarks) {
      docsChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-remarks' },
        children: [
          {
            type: 'element',
            tagName: 'div',
            properties: { class: 'twohash-popup-section-label' },
            children: [{ type: 'text', value: 'Remarks' }],
          },
          { type: 'text', value: docs.remarks },
        ],
      })
    }

    if (docs.examples && docs.examples.length > 0) {
      const exampleNodes: HastNode[] = docs.examples.map((ex: string) => ({
        type: 'element' as const,
        tagName: 'pre',
        properties: {},
        children: [{ type: 'text' as const, value: ex }],
      }))

      docsChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-example' },
        children: [
          {
            type: 'element',
            tagName: 'div',
            properties: { class: 'twohash-popup-section-label' },
            children: [{ type: 'text', value: 'Examples' }],
          },
          ...exampleNodes,
        ],
      })
    }

    if (docs.exceptions && docs.exceptions.length > 0) {
      const exceptionItems: HastNode[] = docs.exceptions.map((e: TwohashDocException) => ({
        type: 'element' as const,
        tagName: 'div',
        properties: { class: 'twohash-popup-exception' },
        children: [
          {
            type: 'element' as const,
            tagName: 'span',
            properties: { class: 'twohash-popup-exception-type' },
            children: [{ type: 'text' as const, value: e.type }],
          },
          { type: 'text' as const, value: ` — ${e.text}` },
        ],
      }))

      docsChildren.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-exceptions' },
        children: [
          {
            type: 'element',
            tagName: 'div',
            properties: { class: 'twohash-popup-section-label' },
            children: [{ type: 'text', value: 'Exceptions' }],
          },
          ...exceptionItems,
        ],
      })
    }

    if (docsChildren.length > 0) {
      sections.push({
        type: 'element',
        tagName: 'div',
        properties: { class: 'twohash-popup-docs' },
        children: docsChildren,
      })
    }

    return sections
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

class TwohashCompletionAnnotation {
  readonly completion: TwohashCompletion
  readonly inlineRange: { columnStart: number; columnEnd: number }

  constructor(completion: TwohashCompletion) {
    this.completion = completion
    this.inlineRange = {
      columnStart: completion.character,
      columnEnd: completion.character,
    }
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    const items: HastNode[] = this.completion.items.map(item => ({
      type: 'element' as const,
      tagName: 'li',
      properties: { class: `twohash-completion-item twohash-completion-kind-${item.kind}` },
      children: [
        {
          type: 'element',
          tagName: 'span',
          properties: { class: 'twohash-completion-kind' },
          children: [{ type: 'text', value: item.kind }],
        },
        {
          type: 'element',
          tagName: 'span',
          properties: { class: 'twohash-completion-label' },
          children: [{ type: 'text', value: item.label }],
        },
        ...(item.detail ? [{
          type: 'element' as const,
          tagName: 'span',
          properties: { class: 'twohash-completion-detail' },
          children: [{ type: 'text', value: item.detail }],
        }] : []),
      ],
    }))

    const completionList: HastNode = {
      type: 'element',
      tagName: 'ul',
      properties: { class: 'twohash-completion-list' },
      children: items,
    }

    return [...nodesToTransform, completionList]
  }
}

interface HastNode {
  type: string
  tagName?: string
  properties?: Record<string, unknown>
  children?: HastNode[]
  value?: string
}
