import { createGloSharp, type GloSharpOptions, type GloSharpResult, type GloSharpDisplayPart } from '@glosharp/core'
import { createHash } from 'node:crypto'
import type { ShikiTransformer } from 'shiki'

export interface TransformerGloSharpOptions extends GloSharpOptions {
  project?: string
  region?: string
}

export interface GloSharpCodeBlock {
  code: string
  project?: string
  region?: string
}

export type GloSharpResultMap = Map<string, GloSharpResult>

// Pre-processed transformer that takes an already-computed result.
// This is the recommended approach since Shiki's preprocess hook is synchronous.
export function transformerGloSharpWithResult(result: GloSharpResult): ShikiTransformer {
  return {
    name: 'glosharp',

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
// Use with processGloSharpBlocks() for the recommended two-step pattern.
export function transformerGloSharpFromMap(resultMap: GloSharpResultMap): ShikiTransformer {
  let currentResult: GloSharpResult | undefined

  return {
    name: 'glosharp',

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
export async function processGloSharpBlocks(
  blocks: Array<string | GloSharpCodeBlock>,
  options: TransformerGloSharpOptions = {},
): Promise<GloSharpResultMap> {
  const glosharp = createGloSharp(options)
  const resultMap: GloSharpResultMap = new Map()

  const tasks = blocks
    .map(block => {
      const code = typeof block === 'string' ? block : block.code
      const project = typeof block === 'string' ? options.project : (block.project ?? options.project)
      const region = typeof block === 'string' ? options.region : (block.region ?? options.region)
      return { code, project, region }
    })
  const results = await Promise.all(
    tasks.map(({ code, project, region }) =>
      glosharp.process({ code, project, region }).then(result => ({
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
export async function processGloSharpCode(
  code: string,
  options: TransformerGloSharpOptions = {},
): Promise<GloSharpResult | null> {
  const glosharp = createGloSharp(options)
  return glosharp.process({ code, project: options.project, region: options.region })
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

function injectHovers(root: HastElement, result: GloSharpResult): void {
  const lines = findCodeLines(root)

  // Group hovers by line, then process each line's hovers right-to-left
  // so that injected popup nodes don't shift column positions of earlier hovers.
  const hoversByLine = new Map<number, Array<{ hover: GloSharpResult['hovers'][0]; index: number }>>()
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

      const partNodes: HastNode[] = hover.parts.map((part: GloSharpDisplayPart) =>
        h('span', { class: `glosharp-${part.kind}` }, [hText(part.text)])
      )

      const popupChildren: HastNode[] = [
        h('code', { class: 'glosharp-popup-code' }, partNodes),
      ]

      if (hover.docs?.summary) {
        popupChildren.push(
          h('div', { class: 'glosharp-popup-docs' }, [hText(hover.docs.summary)])
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
      class: 'glosharp-error-code',
      href: `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/${code.toLowerCase()}`,
      target: '_blank',
      rel: 'noopener',
    }, [hText(code)])
  }
  return h('span', { class: 'glosharp-error-code' }, [hText(code)])
}

function injectErrors(root: HastElement, result: GloSharpResult): void {
  const lines = findCodeLines(root)

  for (const error of result.errors) {
    const severityClass = `glosharp-severity-${error.severity}`

    const errorMessage = h('div', { class: `glosharp-error-message ${severityClass}` }, [
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

function injectCompletions(root: HastElement, result: GloSharpResult): void {
  if (!result.completions || result.completions.length === 0) return

  const lines = findCodeLines(root)

  for (const completion of result.completions) {
    const line = lines[completion.line]
    if (!line) continue

    const items = completion.items.map(item =>
      h('li', { class: `glosharp-completion-item glosharp-completion-kind-${item.kind}` }, [
        h('span', { class: 'glosharp-completion-kind' }, [hText(item.kind)]),
        h('span', { class: 'glosharp-completion-label' }, [hText(item.label)]),
        ...(item.detail ? [h('span', { class: 'glosharp-completion-detail' }, [hText(item.detail)])] : []),
      ])
    )

    const completionList = h('ul', { class: 'glosharp-completion-list' }, items)

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
  const hoverClass = persistent ? 'glosharp-hover glosharp-hover-persistent' : 'glosharp-hover'
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
          class: 'glosharp-popup',
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
        class: 'glosharp-hover',
        style: `anchor-name: ${anchorName}`,
      }, [hoverSpan])
      parts.push(wrapper)

      const popup = h('div', {
        class: 'glosharp-popup',
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
