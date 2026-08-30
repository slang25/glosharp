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

const OPENING = /^( {0,3})(`{3,}|~{3,})[ \t]*(.*)$/

/**
 * Find fenced code blocks in a Markdown document.
 *
 * A hand-rolled scanner rather than a Markdown parser: the artifact key is the
 * fence body byte-for-byte, and every AST library normalises something. Follows
 * CommonMark for the parts that decide where a body starts and ends (fence
 * length, indentation stripping, tilde fences, unterminated fences).
 */
export function findFences(markdown: string, lang?: string): FenceBlock[] {
  const lines = markdown.split('\n')
  const blocks: FenceBlock[] = []

  for (let i = 0; i < lines.length; i++) {
    const open = OPENING.exec(lines[i])
    if (!open) continue

    const [, indent, marker, info] = open
    // A backtick info string may not contain a backtick (it would be inline code).
    if (marker[0] === '`' && info.includes('`')) continue

    const body: string[] = []
    let closed = false
    let j = i + 1
    for (; j < lines.length; j++) {
      if (isClosingFence(lines[j], marker)) {
        closed = true
        break
      }
      body.push(stripIndent(lines[j], indent.length))
    }

    const [langToken, ...rest] = info.trim().split(/[ \t]+/)
    if (!lang || langToken?.toLowerCase() === lang.toLowerCase()) {
      blocks.push({
        lang: langToken ?? '',
        attributes: parseFenceAttributes(rest.join(' ')),
        code: body.join('\n'),
        line: i + 1,
      })
    }

    // Resume after the closing fence; an unterminated fence runs to EOF.
    i = closed ? j : lines.length
  }

  return blocks
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

function isClosingFence(line: string, marker: string): boolean {
  const match = /^ {0,3}(`{3,}|~{3,})[ \t]*$/.exec(line)
  if (!match) return false
  const closing = match[1]
  return closing[0] === marker[0] && closing.length >= marker.length
}

function stripIndent(line: string, amount: number): string {
  let removed = 0
  while (removed < amount && (line[removed] === ' ' || line[removed] === '\t')) removed++
  return line.slice(removed)
}
