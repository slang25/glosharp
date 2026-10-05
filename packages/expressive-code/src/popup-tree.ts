import { h, s, type Element, type ElementContent } from '@expressive-code/core/hast'

/**
 * Popup content as a compact JSON tree. The server renders it to HAST for
 * always-visible `^?` results; hover popups ship it as data and the client
 * module builds their DOM on first use (`buildPopupNode` in client.ts mirrors
 * `popupTreeToHast`). Keeping hover popups out of the page's DOM roughly halves
 * its element count.
 *
 * A node is a string (text) or `[selector, props?, ...children]`, where the
 * selector is `tag.class1.class2` and `props` an optional object of attributes.
 */
export type PopupNode = string | PopupElement
export type PopupProps = Record<string, string>
export type PopupElement = [selector: string, ...rest: (PopupNode | PopupProps)[]]

const SVG_TAGS = new Set(['svg', 'use', 'path'])

export function popupTreeToHast(node: PopupNode): ElementContent {
  if (typeof node === 'string') return { type: 'text', value: node }
  const [selector, ...rest] = node
  const tag = selector.split('.', 1)[0]
  let props: PopupProps = {}
  const children: ElementContent[] = []
  for (const item of rest) {
    if (isProps(item)) props = item
    else children.push(popupTreeToHast(item))
  }
  return (SVG_TAGS.has(tag) ? s(selector, props, children) : h(selector, props, children)) as Element
}

function isProps(item: PopupNode | PopupProps): item is PopupProps {
  return typeof item === 'object' && !Array.isArray(item)
}
