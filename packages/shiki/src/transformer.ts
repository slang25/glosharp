import { createTwohash, type TwohashOptions, type TwohashResult, type TwohashDisplayPart } from '@slang25/twohash'
import { createHash } from 'node:crypto'
import type { ShikiTransformer } from 'shiki'

export interface TransformerTwohashOptions extends TwohashOptions {
  project?: string
  region?: string
}

export interface TwohashCodeBlock {
  code: string
  project?: string
  region?: string
}

export type TwohashResultMap = Map<string, TwohashResult>

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
      injectCompletions(hast as HastElement, result)
    },
  }
}

// Map-based transformer: looks up pre-computed results by hashing incoming code.
// Use with processTwohashBlocks() for the recommended two-step pattern.
export function transformerTwohashFromMap(resultMap: TwohashResultMap): ShikiTransformer {
  let currentResult: TwohashResult | undefined

  return {
    name: 'twohash',

    preprocess(code) {
      const hash = createHash('sha256').update(code).digest('hex')
      currentResult = resultMap.get(hash)
      return currentResult?.code
    },

    root(hast) {
      if (!currentResult) return
      const result = currentResult
      currentResult = undefined
      injectHovers(hast as HastElement, result)
      injectErrors(hast as HastElement, result)
      injectCompletions(hast as HastElement, result)
    },
  }
}

// Batch-process multiple code blocks concurrently, returning a result map keyed by code hash.
export async function processTwohashBlocks(
  blocks: Array<string | TwohashCodeBlock>,
  options: TransformerTwohashOptions = {},
): Promise<TwohashResultMap> {
  const twohash = createTwohash(options)
  const resultMap: TwohashResultMap = new Map()

  const tasks = blocks
    .map(block => {
      const code = typeof block === 'string' ? block : block.code
      const project = typeof block === 'string' ? options.project : (block.project ?? options.project)
      const region = typeof block === 'string' ? options.region : (block.region ?? options.region)
      return { code, project, region }
    })
  const results = await Promise.all(
    tasks.map(({ code, project, region }) =>
      twohash.process({ code, project, region }).then(result => ({
        hash: createHash('sha256').update(code).digest('hex'),
        result,
      }))
    )
  )

  for (const { hash, result } of results) {
    resultMap.set(hash, result)
  }

  return resultMap
}

// Helper to pre-process code blocks before running Shiki
export async function processTwohashCode(
  code: string,
  options: TransformerTwohashOptions = {},
): Promise<TwohashResult | null> {
  const twohash = createTwohash(options)
  return twohash.process({ code, project: options.project, region: options.region })
}

// Global counter ensures unique CSS anchor names across multiple code blocks on a page.
let anchorCounter = 0

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

  // Group hovers by line, then process each line's hovers right-to-left
  // so that injected popup nodes don't shift column positions of earlier hovers.
  const hoversByLine = new Map<number, Array<{ hover: TwohashResult['hovers'][0]; index: number }>>()
  for (let i = 0; i < result.hovers.length; i++) {
    const hover = result.hovers[i]
    let group = hoversByLine.get(hover.line)
    if (!group) {
      group = []
      hoversByLine.set(hover.line, group)
    }
    group.push({ hover, index: i })
  }

  for (const [lineIdx, group] of hoversByLine) {
    const line = lines[lineIdx]
    if (!line) continue

    // Sort right-to-left by character position
    group.sort((a, b) => b.hover.character - a.hover.character)

    for (const { hover } of group) {
      const anchorName = `--th-${anchorCounter++}`

      const partNodes: HastNode[] = hover.parts.map((part: TwohashDisplayPart) =>
        h('span', { class: `twohash-${part.kind}` }, [hText(part.text)])
      )

      const popupChildren: HastNode[] = [
        h('code', { class: 'twohash-popup-code' }, partNodes),
      ]

      if (hover.docs?.summary) {
        popupChildren.push(
          h('div', { class: 'twohash-popup-docs' }, [hText(hover.docs.summary)])
        )
      }

      wrapTokenAtPosition(line, hover.character, hover.length, anchorName, popupChildren, hover.persistent)
    }
  }
}

const CS_CODE_REGEX = /^CS\d+$/

function buildErrorCodeNode(code: string): HastElement {
  if (CS_CODE_REGEX.test(code)) {
    return h('a', {
      class: 'twohash-error-code',
      href: `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/${code.toLowerCase()}`,
      target: '_blank',
      rel: 'noopener',
    }, [hText(code)])
  }
  return h('span', { class: 'twohash-error-code' }, [hText(code)])
}

function injectErrors(root: HastElement, result: TwohashResult): void {
  const lines = findCodeLines(root)

  for (const error of result.errors) {
    const severityClass = `twohash-severity-${error.severity}`

    const errorMessage = h('div', { class: `twohash-error-message ${severityClass}` }, [
      buildErrorCodeNode(error.code),
      hText(': '),
      hText(error.message),
    ])

    if (error.endLine != null && error.endLine > error.line) {
      // Multi-line: place message after last affected line
      const lastLine = lines[error.endLine]
      if (lastLine?.children) {
        lastLine.children.push(errorMessage)
      }
    } else {
      // Single-line
      const line = lines[error.line]
      if (!line) continue
      if (line.children) {
        line.children.push(errorMessage)
      }
    }
  }
}

function injectCompletions(root: HastElement, result: TwohashResult): void {
  if (!result.completions || result.completions.length === 0) return

  const lines = findCodeLines(root)

  for (const completion of result.completions) {
    const line = lines[completion.line]
    if (!line) continue

    const items = completion.items.map(item =>
      h('li', { class: `twohash-completion-item twohash-completion-kind-${item.kind}` }, [
        h('span', { class: 'twohash-completion-kind' }, [hText(item.kind)]),
        h('span', { class: 'twohash-completion-label' }, [hText(item.label)]),
        ...(item.detail ? [h('span', { class: 'twohash-completion-detail' }, [hText(item.detail)])] : []),
      ])
    )

    const completionList = h('ul', { class: 'twohash-completion-list' }, items)

    if (line.children) {
      line.children.push(completionList)
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
  persistent?: boolean,
): void {
  const hoverClass = persistent ? 'twohash-hover twohash-hover-persistent' : 'twohash-hover'
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
      const offsetInToken = targetStart - spanStart
      const tokenText = text

      // If the hover covers the whole token, wrap it directly
      if (offsetInToken === 0 && length === tokenText.length) {
        const wrapper = h('span', {
          class: hoverClass,
          style: `anchor-name: ${anchorName}`,
        }, [child])

        const popup = h('div', {
          class: 'twohash-popup',
          style: `position-anchor: ${anchorName}`,
        }, popupChildren)

        line.children.splice(i, 1, wrapper, popup)
        return
      }

      // Otherwise, split the token: [before][hover target][after]
      const props = child.tagName === 'span' ? { ...(child.properties ?? {}) } : {}
      const parts: HastNode[] = []

      if (offsetInToken > 0) {
        parts.push(h('span', { ...props }, [hText(tokenText.slice(0, offsetInToken))]))
      }

      const hoverSpan = h('span', { ...props }, [hText(tokenText.slice(offsetInToken, offsetInToken + length))])
      const wrapper = h('span', {
        class: 'twohash-hover',
        style: `anchor-name: ${anchorName}`,
      }, [hoverSpan])
      parts.push(wrapper)

      const popup = h('div', {
        class: 'twohash-popup',
        style: `position-anchor: ${anchorName}`,
      }, popupChildren)
      parts.push(popup)

      const afterStart = offsetInToken + length
      if (afterStart < tokenText.length) {
        parts.push(h('span', { ...props }, [hText(tokenText.slice(afterStart))]))
      }

      line.children.splice(i, 1, ...parts)
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
