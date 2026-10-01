import type { Element, ElementContent, Properties, Text } from 'hast'
import type {
  GloSharpCompletion,
  GloSharpCompletionItem,
  GloSharpDocComment,
  GloSharpError,
  GloSharpHover,
  GloSharpResult,
  GloSharpTag,
} from '@glosharp/core'

export interface GloSharpRenderOptions {
  /**
   * Make hover targets keyboard-focusable (`tabindex="0"`); the popup then also
   * opens on focus. Default `true`.
   */
  focusable?: boolean
  /** Maximum completion items shown per `^|` query. Default `12`. */
  completionLimit?: number
}

export interface RenderTarget {
  /** The tree containing the lines (Shiki's root); needed to place after-line blocks. */
  root: { children: ReadonlyArray<{ type: string }> }
  pre?: Element
  /** The line elements, in order (Shiki's `this.lines`). */
  lines: Element[]
}

// Nodes injected by this renderer that are not part of the code text: popups
// and after-line blocks. They are skipped when measuring columns.
const decorations = new WeakSet<ElementContent>()
// Hover wrappers own a popup; splitting one would duplicate it.
const atomic = new WeakSet<ElementContent>()

/** Apply a Glo# result to Shiki's HAST for the same code. */
export function applyGloSharp(target: RenderTarget, result: GloSharpResult, options: GloSharpRenderOptions = {}): void {
  const { lines, pre } = target
  const scope = scopeId(result)
  indexParents(target.root)

  if (pre) decoratePre(pre, result)

  // Order matters: ranges are wrapped before anything is appended after lines,
  // and underlines before hovers so a hover never has to be split.
  applyErrorUnderlines(lines, result.errors)
  applyHovers(lines, result.hovers, scope, options)
  applyHighlights(lines, pre, result)

  const after = new Map<number, ElementContent[]>()
  const push = (line: number, node: Element) => {
    decorations.add(node)
    const list = after.get(line) ?? []
    list.push(node)
    after.set(line, list)
  }

  result.hovers.forEach((hover, index) => {
    if (hover.persistent) push(hover.line, queryBlock(hover, `${scope}-${index}`))
  })
  for (const completion of result.completions) {
    if (completion.items.length > 0) push(completion.line, completionBlock(completion, lines[completion.line], options))
  }
  for (const error of result.errors) push(error.endLine != null && error.endLine > error.line ? error.endLine : error.line, errorBlock(error))
  for (const tag of result.tags) push(tag.line, tagBlock(tag))

  for (const [index, nodes] of after) {
    const line = lines[index] ?? lines[lines.length - 1]
    if (line) insertAfterLine(line, nodes)
  }
}

// ---- pre / theme ----

function decoratePre(pre: Element, result: GloSharpResult): void {
  addClass(pre, 'glosharp')
  const style = typeof pre.properties.style === 'string' ? pre.properties.style : ''
  const bg = /(?:^|;)\s*background-color:\s*([^;]+)/.exec(style)?.[1] ?? /--shiki-light-bg:\s*([^;]+)/.exec(style)?.[1]
  const fg = /(?:^|;)\s*color:\s*([^;]+)/.exec(style)?.[1] ?? /--shiki-light:\s*([^;]+)/.exec(style)?.[1]

  // Popups, query boxes and messages inherit the code block's own colours.
  const vars: string[] = []
  if (bg) vars.push(`--glosharp-bg:${bg.trim()}`)
  if (fg) vars.push(`--glosharp-fg:${fg.trim()}`)
  if (vars.length) pre.properties.style = style ? `${style.replace(/;?\s*$/, ';')}${vars.join(';')}` : vars.join(';')

  // Part colours come in a light and a dark palette; pick by the default theme's background.
  addClass(pre, bg && isDark(bg) ? 'glosharp-theme-dark' : 'glosharp-theme-light')
  if (/--shiki-dark/.test(style)) addClass(pre, 'glosharp-dual')
  if (result.highlights.some((h) => h.kind === 'focus')) addClass(pre, 'glosharp-has-focus')
}

function isDark(color: string): boolean {
  const hex = /^#([0-9a-f]{3,8})$/i.exec(color.trim())?.[1]
  if (!hex) return false
  const full = hex.length <= 4 ? [...hex.slice(0, 3)].map((c) => c + c).join('') : hex.slice(0, 6)
  const [r, g, b] = [0, 2, 4].map((i) => parseInt(full.slice(i, i + 2), 16) / 255)
  return 0.2126 * r + 0.7152 * g + 0.0722 * b < 0.5
}

// ---- hovers ----

function applyHovers(lines: Element[], hovers: GloSharpHover[], scope: string, options: GloSharpRenderOptions): void {
  const focusable = options.focusable !== false
  hovers.forEach((hover, index) => {
    const line = lines[hover.line]
    if (!line || hover.length <= 0) return
    const id = `${scope}-${index}`
    const anchor = `--${id}`

    if (hover.persistent) {
      // The query result is always visible below the line; the token just points at it.
      wrapRange(line, hover.character, hover.character + hover.length, (children) =>
        h('span', { class: 'glosharp-hover glosharp-hover-persistent', 'aria-describedby': id }, children),
      )
      return
    }

    const popup = h(
      'span',
      { class: 'glosharp-popup', role: 'tooltip', id, style: `position-anchor:${anchor}` },
      popupContent(hover),
    )
    decorations.add(popup)

    wrapRange(line, hover.character, hover.character + hover.length, (children) => {
      const wrapper = h(
        'span',
        {
          class: 'glosharp-hover',
          style: `anchor-name:${anchor}`,
          ...(focusable ? { tabindex: '0', 'aria-describedby': id } : {}),
        },
        [...children, popup],
      )
      atomic.add(wrapper)
      return wrapper
    })
  })
}

function popupContent(hover: GloSharpHover): ElementContent[] {
  const signature: ElementContent[] = hover.parts.map((part) =>
    h('span', { class: `glosharp-part glosharp-${part.kind}` }, [text(part.text)]),
  )
  if (hover.overloadCount && hover.overloadCount > 0) {
    signature.push(
      h('span', { class: 'glosharp-overloads' }, [
        text(` (+ ${hover.overloadCount} overload${hover.overloadCount === 1 ? '' : 's'})`),
      ]),
    )
  }

  const content: ElementContent[] = [h('code', { class: 'glosharp-popup-code' }, signature)]

  if (hover.typeAnnotations && hover.typeAnnotations.length > 0) {
    content.push(
      h('span', { class: 'glosharp-popup-types' }, [
        h('span', { class: 'glosharp-popup-types-header' }, [text('Types:')]),
        ...hover.typeAnnotations.map((a) =>
          h('span', { class: 'glosharp-type-annotation' }, [
            h('span', { class: 'glosharp-part glosharp-className' }, [text(a.name)]),
            text(' is '),
            h('span', { class: 'glosharp-type-expansion' }, [text(a.expansion)]),
          ]),
        ),
      ]),
    )
  }

  content.push(...docsContent(hover.docs))
  return content
}

function docsContent(docs: GloSharpDocComment | null | undefined): ElementContent[] {
  if (!docs) return []
  const sections: ElementContent[] = []
  if (docs.summary) sections.push(h('span', { class: 'glosharp-popup-docs' }, [text(docs.summary)]))

  const rows: ElementContent[] = []
  for (const param of docs.params ?? []) {
    rows.push(docSection('param', [h('span', { class: 'glosharp-doc-name' }, [text(param.name)]), text(` — ${param.text}`)]))
  }
  if (docs.returns) rows.push(docSection('returns', [text(docs.returns)]))
  for (const exception of docs.exceptions ?? []) {
    rows.push(docSection('throws', [h('span', { class: 'glosharp-doc-name' }, [text(exception.type)]), text(` — ${exception.text}`)]))
  }
  if (docs.remarks) rows.push(docSection('remarks', [text(docs.remarks)]))
  if (rows.length) sections.push(h('span', { class: 'glosharp-popup-doc-tags' }, rows))
  return sections
}

function docSection(label: string, body: ElementContent[]): Element {
  return h('span', { class: `glosharp-doc glosharp-doc-${label}` }, [
    h('span', { class: 'glosharp-doc-label' }, [text(`@${label}`)]),
    text(' '),
    ...body,
  ])
}

function queryBlock(hover: GloSharpHover, id: string): Element {
  return h('span', { class: 'glosharp-query', style: `--glosharp-col:${hover.character}` }, [
    h('span', { class: 'glosharp-query-box', id, role: 'note' }, popupContent(hover)),
  ])
}

// ---- errors ----

function applyErrorUnderlines(lines: Element[], errors: GloSharpError[]): void {
  for (const error of errors) {
    const severity = `glosharp-severity-${error.severity}`
    const underline = (children: ElementContent[]) =>
      h('span', { class: `glosharp-error-underline ${severity}` }, children)

    const endLine = error.endLine != null && error.endLine > error.line ? error.endLine : error.line
    for (let index = error.line; index <= endLine; index++) {
      const line = lines[index]
      if (!line) continue
      const width = lineLength(line)
      const start = index === error.line ? error.character : leadingWhitespace(line)
      let end =
        endLine === error.line
          ? error.character + error.length
          : index === endLine
            ? (error.endCharacter ?? width)
            : width
      // Zero-length diagnostics (a missing `;`) still deserve a visible mark.
      if (end <= start) end = start + 1
      wrapRange(line, Math.min(start, width), Math.min(end, width), underline)
    }
  }
}

const CS_CODE = /^CS\d+$/

function errorBlock(error: GloSharpError): Element {
  const code = CS_CODE.test(error.code)
    ? h(
        'a',
        {
          class: 'glosharp-error-code',
          href: `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/${error.code.toLowerCase()}`,
          target: '_blank',
          rel: ['noopener'],
        },
        [text(error.code)],
      )
    : h('span', { class: 'glosharp-error-code' }, [text(error.code)])

  return h(
    'span',
    {
      class: `glosharp-error-message glosharp-severity-${error.severity}${error.expected ? ' glosharp-error-expected' : ''}`,
    },
    [code, text(': '), text(error.message)],
  )
}

// ---- completions ----

function completionBlock(completion: GloSharpCompletion, line: Element | undefined, options: GloSharpRenderOptions): Element {
  const limit = options.completionLimit ?? 12
  const prefix = line ? identifierPrefix(textContent(line), completion.character) : ''
  const items = filterCompletions(completion.items, prefix)
  const shown = items.slice(0, limit)

  const list = h(
    'span',
    { class: 'glosharp-completion-list', role: 'list' },
    shown.map((item) => completionItem(item, prefix)),
  )
  if (items.length > shown.length) {
    list.children.push(h('span', { class: 'glosharp-completion-more' }, [text(`… ${items.length - shown.length} more`)]))
  }

  return h('span', { class: 'glosharp-completions', style: `--glosharp-col:${completion.character}` }, [list])
}

function completionItem(item: GloSharpCompletionItem, prefix: string): Element {
  const label: ElementContent[] =
    prefix && item.label.toLowerCase().startsWith(prefix.toLowerCase())
      ? [h('span', { class: 'glosharp-completion-match' }, [text(item.label.slice(0, prefix.length))]), text(item.label.slice(prefix.length))]
      : [text(item.label)]
  return h('span', { class: `glosharp-completion-item glosharp-completion-kind-${item.kind}`, role: 'listitem' }, [
    h('span', { class: 'glosharp-completion-kind', title: item.kind }, [text(kindBadge(item.kind))]),
    h('span', { class: 'glosharp-completion-label' }, label),
    ...(item.detail ? [h('span', { class: 'glosharp-completion-detail' }, [text(item.detail)])] : []),
  ])
}

/** The identifier fragment typed before the caret (`list.Ad` → `Ad`). */
function identifierPrefix(lineText: string, character: number): string {
  return /[\p{L}\p{N}_]*$/u.exec(lineText.slice(0, character))?.[0] ?? ''
}

/**
 * Items matching the typed prefix (all items when nothing matches or nothing
 * was typed), without duplicates.
 */
export function filterCompletions(items: GloSharpCompletionItem[], prefix: string): GloSharpCompletionItem[] {
  const seen = new Set<string>()
  const unique = items.filter((item) => {
    const key = `${item.kind}\u0000${item.label}`
    if (seen.has(key)) return false
    seen.add(key)
    return true
  })
  if (!prefix) return unique
  const lower = prefix.toLowerCase()
  const matching = unique.filter((item) => item.label.toLowerCase().startsWith(lower))
  return matching.length > 0 ? matching : unique
}

function kindBadge(kind: string): string {
  const badges: Record<string, string> = {
    Method: 'M', ExtensionMethod: 'M', Property: 'P', Field: 'F', Event: 'E', Class: 'C', Struct: 'S',
    Interface: 'I', Enum: 'E', EnumMember: 'E', Delegate: 'D', Namespace: 'N', Local: 'L', Parameter: 'P',
    Keyword: 'K', Snippet: '⧉', Constant: 'C', TypeParameter: 'T', Operator: 'O',
  }
  return badges[kind] ?? kind.charAt(0).toUpperCase()
}

// ---- tags & highlights ----

function tagBlock(tag: GloSharpTag): Element {
  return h('span', { class: `glosharp-tag glosharp-tag-${tag.name}`, role: 'note' }, [
    h('span', { class: 'glosharp-tag-name' }, [text(tag.name)]),
    text(tag.text),
  ])
}

function applyHighlights(lines: Element[], pre: Element | undefined, result: GloSharpResult): void {
  const focused = new Set(result.highlights.filter((h) => h.kind === 'focus').map((h) => h.line))
  for (const highlight of result.highlights) {
    const line = lines[highlight.line]
    if (!line) continue
    if (highlight.kind === 'highlight') addClass(line, 'glosharp-highlight')
    else if (highlight.kind === 'add') addClass(line, 'glosharp-diff-add')
    else if (highlight.kind === 'remove') addClass(line, 'glosharp-diff-remove')
    else if (highlight.kind === 'focus') addClass(line, 'glosharp-focused')
  }
  if (focused.size > 0) {
    lines.forEach((line, index) => {
      if (!focused.has(index)) addClass(line, 'glosharp-focus-dim')
    })
    if (pre) addClass(pre, 'glosharp-has-focus')
  }
}

// ---- after-line insertion ----

/**
 * Put block-level annotations on their own rows after a line, without adding a
 * blank row: Shiki separates lines with "\n" text nodes, so the nodes go after
 * that newline (a newline *after* a block would render as an empty line).
 */
function insertAfterLine(line: Element, nodes: ElementContent[]): void {
  const parent = findParent(line)
  if (!parent) {
    line.children.push(...nodes)
    return
  }
  let index = parent.children.indexOf(line) + 1
  const next = parent.children[index]
  if (next && next.type === 'text' && next.value.startsWith('\n')) {
    if (next.value.length > 1) {
      // Keep any extra text after the newline (shouldn't happen with Shiki).
      parent.children.splice(index, 1, text('\n'), text(next.value.slice(1)))
    }
    index++
  }
  parent.children.splice(index, 0, ...nodes)
}

const parents = new WeakMap<Element, Element>()

/** Index line → parent once per render (lines live in <code>, or nested in custom structures). */
function indexParents(root: { children: ReadonlyArray<{ type: string }> }): void {
  const visit = (node: { children: ReadonlyArray<{ type: string }> }) => {
    for (const child of node.children) {
      if (child.type === 'element') {
        parents.set(child as Element, node as Element)
        visit(child as Element)
      }
    }
  }
  visit(root)
}

function findParent(node: Element): Element | undefined {
  return parents.get(node)
}

// ---- range wrapping ----

function lineLength(line: Element): number {
  return textContent(line).length
}

function leadingWhitespace(line: Element): number {
  return /^[ \t]*/.exec(textContent(line))?.[0].length ?? 0
}

/** Code text of a node, skipping injected popups and blocks. */
export function textContent(node: ElementContent): string {
  if (decorations.has(node)) return ''
  if (node.type === 'text') return node.value
  if (node.type === 'element') return node.children.map(textContent).join('')
  return ''
}

/**
 * Wrap the line's code text in [start, end) with `wrap(children)`. Tokens that
 * straddle a boundary are split (their element is cloned for each part, so
 * colours survive). Returns false when the range can't be wrapped.
 */
function wrapRange(line: Element, start: number, end: number, wrap: (children: ElementContent[]) => Element): boolean {
  if (end <= start) return false
  if (!splitAt(line, start) || !splitAt(line, end)) return false

  let col = 0
  let first = -1
  let last = -1
  for (let i = 0; i < line.children.length; i++) {
    const child = line.children[i]
    const length = textContent(child).length
    if (length === 0) continue
    if (col >= start && col + length <= end) {
      if (first === -1) first = i
      last = i
    }
    col += length
  }
  if (first === -1) return false

  const covered = line.children.slice(first, last + 1)
  line.children.splice(first, covered.length, wrap(covered))
  return true
}

/** Ensure a child boundary exists at `offset` in `parent`'s code text. */
function splitAt(parent: Element, offset: number): boolean {
  let col = 0
  for (let i = 0; i < parent.children.length; i++) {
    const child = parent.children[i]
    const length = textContent(child).length
    if (offset <= col) return true
    if (offset < col + length) {
      const inner = offset - col
      if (child.type === 'text') {
        parent.children.splice(i, 1, text(child.value.slice(0, inner)), text(child.value.slice(inner)))
        return true
      }
      if (child.type !== 'element' || atomic.has(child)) return false
      if (!splitAt(child, inner)) return false
      // Partition the element's children at the (now existing) boundary.
      let acc = 0
      let cut = 0
      while (cut < child.children.length && acc < inner) {
        acc += textContent(child.children[cut]).length
        cut++
      }
      const left: Element = { ...child, properties: { ...child.properties }, children: child.children.slice(0, cut) }
      const right: Element = { ...child, properties: { ...child.properties }, children: child.children.slice(cut) }
      parent.children.splice(i, 1, left, right)
      return true
    }
    col += length
  }
  return true
}

// ---- utilities ----

function h(tagName: string, properties: Properties, children: ElementContent[]): Element {
  return { type: 'element', tagName, properties, children }
}

function text(value: string): Text {
  return { type: 'text', value }
}

function addClass(node: Element, className: string): void {
  const existing = node.properties.class ?? node.properties.className
  const list = Array.isArray(existing)
    ? existing.map(String)
    : typeof existing === 'string'
      ? existing.split(/\s+/).filter(Boolean)
      : []
  if (!list.includes(className)) list.push(className)
  delete node.properties.className
  node.properties.class = list.join(' ')
}

/** True when a HAST element carries `className` (string or array class forms). */
export function hasClass(node: Element, className: string): boolean {
  const value = node.properties?.class ?? node.properties?.className
  if (Array.isArray(value)) return value.map(String).includes(className)
  return typeof value === 'string' && value.split(/\s+/).includes(className)
}

/**
 * Deterministic, page-unique-enough prefix for ids and CSS anchor names:
 * derived from the result, so rebuilds produce identical HTML.
 */
function scopeId(result: GloSharpResult): string {
  // FNV-1a over the code and hover positions; collisions only matter for two
  // *different* blocks on one page, and `anchor-scope` on the <pre> covers
  // identical ones.
  let hash = 0x811c9dc5
  const input = `${result.code}\u0000${result.hovers.map((x) => `${x.line}:${x.character}:${x.length}`).join(',')}`
  for (let i = 0; i < input.length; i++) {
    hash ^= input.charCodeAt(i)
    hash = Math.imul(hash, 0x01000193)
  }
  return `gs${(hash >>> 0).toString(36)}`
}
