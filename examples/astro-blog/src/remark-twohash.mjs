import { processTwohashCode, transformerTwohashWithResult } from '@twohash/shiki'
import { visit } from 'unist-util-visit'

/**
 * Shared state: remark plugin stores results here, Shiki transformer reads them.
 * Keyed by the original code string.
 */
export const twohashResults = new Map()

/**
 * Remark plugin that pre-processes C# code blocks containing twohash markers.
 * Must run before Shiki so the results are available in the synchronous transformer.
 */
export function remarkTwohash() {
  return async (tree) => {
    const codeNodes = []
    visit(tree, 'code', (node) => {
      if ((node.lang === 'csharp' || node.lang === 'cs') && node.value) {
        codeNodes.push(node)
      }
    })

    for (const node of codeNodes) {
      const result = await processTwohashCode(node.value)
      if (result) {
        node.value = result.code
        twohashResults.set(result.code, result)
      }
    }
  }
}

/**
 * Shiki transformer that applies twohash hover/error annotations.
 * Looks up pre-computed results from the remark plugin.
 */
export function twohashTransformer() {
  return {
    name: 'twohash',
    preprocess(code) {
      const result = twohashResults.get(code)
      if (result) {
        this.__twohashResult = result
        twohashResults.delete(code)
      }
    },
    root(hast) {
      if (!this.__twohashResult) return
      const inner = transformerTwohashWithResult(this.__twohashResult)
      inner.root.call(this, hast)
      this.__twohashResult = undefined
    },
  }
}
