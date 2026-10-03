/**
 * Minimal line operations of an Expressive Code block that `syncLines` needs.
 * (`ExpressiveCodeBlock` satisfies this.)
 */
export interface EditableLines {
  getLines(): readonly { text: string; editText(columnStart: number | undefined, columnEnd: number | undefined, newText: string): string }[]
  deleteLines(indices: number[]): void
  insertLines(index: number, textLines: string[]): unknown
}

const stripCr = (s: string) => (s.endsWith('\r') ? s.slice(0, -1) : s)

/**
 * Pairs each target line with an existing line of identical text, preserving
 * order (longest common subsequence). Returns `match[targetIndex] = sourceIndex`
 * or -1 for target lines that have no counterpart.
 */
export function alignLines(source: string[], target: string[]): number[] {
  const a = source.map(stripCr)
  const b = target.map(stripCr)
  const match = new Array<number>(b.length).fill(-1)

  // Common prefix / suffix are matched directly (the usual case: only marker,
  // directive and cut lines are removed), keeping the DP table small.
  let start = 0
  while (start < a.length && start < b.length && a[start] === b[start]) {
    match[start] = start
    start++
  }
  let endA = a.length
  let endB = b.length
  while (endA > start && endB > start && a[endA - 1] === b[endB - 1]) {
    endA--
    endB--
    match[endB] = endA
  }

  const n = endA - start
  const m = endB - start
  if (n === 0 || m === 0) return match

  // lcs[i][j] = LCS length of a[start+i..endA) and b[start+j..endB)
  const lcs: Uint32Array[] = Array.from({ length: n + 1 }, () => new Uint32Array(m + 1))
  for (let i = n - 1; i >= 0; i--) {
    for (let j = m - 1; j >= 0; j--) {
      lcs[i][j] = a[start + i] === b[start + j]
        ? lcs[i + 1][j + 1] + 1
        : Math.max(lcs[i + 1][j], lcs[i][j + 1])
    }
  }
  let i = 0
  let j = 0
  while (i < n && j < m) {
    if (a[start + i] === b[start + j]) {
      match[start + j] = start + i
      i++
      j++
    } else if (lcs[i + 1][j] >= lcs[i][j + 1]) {
      i++
    } else {
      j++
    }
  }
  return match
}

/**
 * Makes the block's lines equal `target` while keeping the existing line
 * objects for every line that survives. Other plugins (EC's line markers
 * `{1-3}`, `ins=`/`del=` line ranges, `collapse={…}`) attach to line objects
 * during `preprocessMetadata`, so replacing every line would silently drop
 * them. Removed lines (glosharp markers, directives, cut sections) are deleted;
 * lines without a counterpart are inserted.
 */
export function syncLines(codeBlock: EditableLines, target: string[]): void {
  const existing = codeBlock.getLines()
  const match = alignLines(existing.map(l => l.text), target)

  const kept = new Set(match.filter(i => i >= 0))
  const toDelete: number[] = []
  for (let i = 0; i < existing.length; i++) {
    if (!kept.has(i)) toDelete.push(i)
  }
  if (toDelete.length > 0) codeBlock.deleteLines(toDelete)

  // Surviving lines are now in target order; insert the missing ones and
  // normalise text (e.g. trailing \r) in place.
  for (let t = 0; t < target.length; t++) {
    const text = stripCr(target[t])
    if (match[t] < 0) {
      codeBlock.insertLines(t, [text])
    } else {
      const line = codeBlock.getLines()[t]
      if (line.text !== text) line.editText(undefined, undefined, text)
    }
  }
}
