import { createTwohash, type TwohashOptions, type TwohashResult, type TwohashDisplayPart } from 'twohash'
import type { ShikiTransformer } from 'shiki'

export interface TransformerTwohashOptions extends TwohashOptions {}

const TWOHASH_MARKER_REGEX = /\/\/\s*\^[?|]|\/\/\s*@errors:|\/\/\s*@noErrors|\/\/\s*---cut---|\/\/\s*@hide|\/\/\s*@show/

function hasMarkers(code: string): boolean {
  return TWOHASH_MARKER_REGEX.test(code)
}

// Pre-processed transformer that takes an already-computed result.
// This is the recommended approach since Shiki's preprocess hook is synchronous.
export function transformerTwohashWithResult(result: TwohashResult): ShikiTransformer {
  return {
    name: 'twohash',

    preprocess() {
      return result.code
    },

    root(hast) {
      injectHovers(hast as HastElement, result)
      injectErrors(hast as HastElement, result)
    },
  }
}

// Convenience wrapper: detects markers and processes via CLI, then applies results.
// Since Shiki preprocess is sync, this requires passing a pre-built result map.
export function transformerTwohash(options: TransformerTwohashOptions = {}): ShikiTransformer {
  const twohash = createTwohash(options)
  let pendingResult: TwohashResult | undefined

  return {
    name: 'twohash',

    // Note: Shiki preprocess is synchronous, but CLI spawning is async.
    // Users should call processCodeBlocks() before codeToHtml() to pre-populate results.
    preprocess(code) {
      // Can't run async here — the result must already be available
      // This is a limitation; use transformerTwohashWithResult for guaranteed behavior
      return undefined
    },

    root(hast) {
      if (!pendingResult) return
      const result = pendingResult
      pendingResult = undefined
      injectHovers(hast as HastElement, result)
      injectErrors(hast as HastElement, result)
    },
  }
}

// Helper to pre-process code blocks before running Shiki
export async function processTwohashCode(
  code: string,
  options: TwohashOptions = {},
): Promise<TwohashResult | null> {
  if (!hasMarkers(code)) return null
  const twohash = createTwohash(options)
  return twohash.process({ code })
}

// ---- HAST manipulation utilities ----

interface HastElement {
  type: string
  tagName?: string
  properties?: Record<string, unknown>
  children?: HastNode[]
  value?: string
}

type HastNode = HastElement

function h(tag: string, props: Record<string, unknown>, children: HastNode[]): HastElement {
  return { type: 'element', tagName: tag, properties: props, children }
}

function hText(value: string): HastElement {
  return { type: 'text', value }
}

function injectHovers(root: HastElement, result: TwohashResult): void {
  const lines = findCodeLines(root)
  let anchorIndex = 0

  for (const hover of result.hovers) {
    const line = lines[hover.line]
    if (!line) continue

    const anchorName = `--th-${anchorIndex++}`

    const partNodes: HastNode[] = hover.parts.map((part: TwohashDisplayPart) =>
      h('span', { class: `twohash-${part.kind}` }, [hText(part.text)])
    )

    const popupChildren: HastNode[] = [
      h('code', { class: 'twohash-popup-code' }, partNodes),
    ]

    if (hover.docs) {
      popupChildren.push(
        h('div', { class: 'twohash-popup-docs' }, [hText(hover.docs)])
      )
    }

    wrapTokenAtPosition(line, hover.character, hover.length, anchorName, popupChildren)
  }
}

function injectErrors(root: HastElement, result: TwohashResult): void {
  const lines = findCodeLines(root)

  for (const error of result.errors) {
    if (error.expected) continue

    const line = lines[error.line]
    if (!line) continue

    const errorMessage = h('div', { class: 'twohash-error-message' }, [
      h('span', { class: 'twohash-error-code' }, [hText(error.code)]),
      hText(': '),
      hText(error.message),
    ])

    if (line.children) {
      line.children.push(errorMessage)
    }
  }
}

function findCodeLines(node: HastElement): HastElement[] {
  const lines: HastElement[] = []
  walkNode(node, (n) => {
    if (n.tagName === 'span' && n.properties?.class === 'line') {
      lines.push(n)
    }
  })
  return lines
}

function walkNode(node: HastElement, visitor: (n: HastElement) => void): void {
  visitor(node)
  if (node.children) {
    for (const child of node.children) {
      if (child.type === 'element') {
        walkNode(child, visitor)
      }
    }
  }
}

function wrapTokenAtPosition(
  line: HastElement,
  character: number,
  length: number,
  anchorName: string,
  popupChildren: HastNode[],
): void {
  if (!line.children) return

  let col = 0
  const targetStart = character
  const targetEnd = character + length

  for (let i = 0; i < line.children.length; i++) {
    const child = line.children[i]
    const text = getTextContent(child)
    const spanStart = col
    const spanEnd = col + text.length

    if (spanStart <= targetStart && spanEnd >= targetEnd) {
      const wrapper = h('span', {
        class: 'twohash-hover',
        style: `anchor-name: ${anchorName}`,
      }, [child])

      const popup = h('div', {
        class: 'twohash-popup',
        style: `position-anchor: ${anchorName}`,
      }, popupChildren)

      line.children.splice(i, 1, wrapper, popup)
      return
    }

    col = spanEnd
  }
}

function getTextContent(node: HastElement): string {
  if (node.value) return node.value
  if (!node.children) return ''
  return node.children.map(getTextContent).join('')
}
