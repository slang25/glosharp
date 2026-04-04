import { processGloSharpCode, transformerGloSharpWithResult } from '@glosharp/shiki'
import { visit } from 'unist-util-visit'

/**
 * Shared state: remark plugin stores results here, Shiki transformer reads them.
 * Keyed by the original code string.
 */
export const glosharpResults = new Map()

/**
 * Remark plugin that pre-processes C# code blocks containing glosharp markers.
 * Must run before Shiki so the results are available in the synchronous transformer.
 */
export function remarkGloSharp() {
  return async (tree) => {
    const codeNodes = []
    visit(tree, 'code', (node) => {
      if ((node.lang === 'csharp' || node.lang === 'cs') && node.value) {
        codeNodes.push(node)
      }
    })

    for (const node of codeNodes) {
      const result = await processGloSharpCode(node.value)
      if (result) {
        node.value = result.code
        glosharpResults.set(result.code, result)
      }
    }
  }
}

/**
 * Shiki transformer that applies glosharp hover/error annotations.
 * Looks up pre-computed results from the remark plugin.
 */
export function glosharpTransformer() {
  return {
    name: 'glosharp',
    preprocess(code) {
      const result = glosharpResults.get(code)
      if (result) {
        this.__glosharpResult = result
        glosharpResults.delete(code)
      }
    },
    root(hast) {
      if (!this.__glosharpResult) return
      const inner = transformerGloSharpWithResult(this.__glosharpResult)
      inner.root.call(this, hast)
      this.__glosharpResult = undefined
    },
  }
}
