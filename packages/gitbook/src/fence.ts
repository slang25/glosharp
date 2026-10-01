/** Fence language that GitBook maps to the Glo# custom block. */
export const DEFAULT_FENCE = 'glosharp'

export type FenceAttributes = Record<string, string | true>

export interface FenceBlock {
  /** Info-string language token, verbatim. */
  lang: string
  /** Remaining info-string tokens, parsed as `key`, `key=value` or `key="value"`. */
  attributes: FenceAttributes
  /** Fence body with the opening fence's indentation removed. */
  code: string
  /** 1-based line number of the opening fence. */
  line: number
}

// Container prefix (blockquote markers), indentation (list item content), the
// fence marker, and the info string.
const OPENING = /^((?:[ \t]{0,3}>[ \t]?)*)([ \t]*)(`{3,}|~{3,})[ \t]*(.*)$/

/**
 * Find fenced code blocks in a Markdown document.
 *
 * A hand-rolled scanner rather than a Markdown parser: the artifact key is the
 * fence body byte-for-byte, and every AST library normalises something. Follows
 * CommonMark for the parts that decide where a body starts and ends (fence
 * length, indentation stripping, tilde fences, unterminated fences).
 *
 * Fences inside containers are found too: inside blockquotes (`> `, nested
 * `> > `) the quote markers are stripped from every body line, and inside list
 * items (indented by the item's content offset, often 2–4+ spaces) the opening
 * fence's indentation is stripped. A fence ends when its container does.
 */
export function findFences(markdown: string, lang?: string): FenceBlock[] {
  // Normalise first: a stray \r makes a closing fence fail to match, so the
  // fence would swallow the rest of the document and every later fence with it.
  const lines = markdown.replace(/\r\n?/g, '\n').split('\n')
  const blocks: FenceBlock[] = []

  for (let i = 0; i < lines.length; i++) {
    const open = OPENING.exec(lines[i])
    if (!open) continue

    const [, quotePrefix, indent, marker, info] = open
    // A backtick info string may not contain a backtick (it would be inline code).
    if (marker[0] === '`' && info.includes('`')) continue
    const depth = (quotePrefix.match(/>/g) ?? []).length

    const body: string[] = []
    let closed = false
    let j = i + 1
    for (; j < lines.length; j++) {
      const inner = stripQuotes(lines[j], depth)
      if (inner === undefined) break // the blockquote ended, and the fence with it
      if (isClosingFence(inner, marker, indent.length)) {
        closed = true
        break
      }
      body.push(stripIndent(inner, indent.length))
    }
    // A container that ended without a closing fence leaves `j` on the line
    // after the body; resume there rather than skipping it.
    if (!closed && j < lines.length) j--

    const [langToken, ...rest] = info.trim().split(/[ \t]+/)
    if (!lang || langToken?.toLowerCase() === lang.toLowerCase()) {
      blocks.push({
        lang: langToken ?? '',
        attributes: parseFenceAttributes(rest.join(' ')),
        code: body.join('\n'),
        line: i + 1,
      })
    }

    // Resume after the closing fence (or where the container ended); an
    // unterminated fence runs to EOF.
    i = j
  }

  return blocks
}

/**
 * Remove `depth` blockquote markers from a line. Returns undefined when the
 * line has fewer markers (the quote, and anything fenced in it, has ended).
 * A blank line inside a quoted fence must still carry its `>`.
 */
function stripQuotes(line: string, depth: number): string | undefined {
  let rest = line
  for (let level = 0; level < depth; level++) {
    const match = /^[ \t]{0,3}>[ \t]?/.exec(rest)
    if (!match) return undefined
    rest = rest.slice(match[0].length)
  }
  return rest
}

/** Parse an info string's attribute tail: `key`, `key=value`, `key="value"`. */
export function parseFenceAttributes(tail: string): FenceAttributes {
  const attributes: FenceAttributes = {}
  const pattern = /([^\s=]+)(?:=(?:"([^"]*)"|'([^']*)'|([^\s]*)))?/g

  for (const match of tail.matchAll(pattern)) {
    const [, key, doubleQuoted, singleQuoted, bare] = match
    const value = doubleQuoted ?? singleQuoted ?? bare
    attributes[key] = value === undefined ? true : value
  }

  return attributes
}

function isClosingFence(line: string, marker: string, indent: number): boolean {
  const match = /^([ \t]*)(`{3,}|~{3,})[ \t]*$/.exec(line)
  if (!match) return false
  // Up to three spaces of extra indentation beyond the opening fence's.
  if (match[1].length > indent + 3) return false
  const closing = match[2]
  return closing[0] === marker[0] && closing.length >= marker.length
}

function stripIndent(line: string, amount: number): string {
  let removed = 0
  while (removed < amount && (line[removed] === ' ' || line[removed] === '\t')) removed++
  return line.slice(removed)
}
