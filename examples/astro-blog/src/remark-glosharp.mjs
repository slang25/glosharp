import { processGloSharpBlocks, transformerGloSharpFromMap } from '@glosharp/shiki'
import { visit } from 'unist-util-visit'

/**
 * Results shared between the two halves of the integration. The remark plugin
 * fills it; the Shiki transformer reads it. Keys are hashes of the code block
 * text, so identical blocks (on one page or across pages) share one result.
 */
const results = new Map()

const CSHARP = new Set(['csharp', 'cs', 'c#'])

/**
 * Shiki transforms synchronously, but glosharp has to run the C# compiler, so
 * the work happens one step earlier: this remark plugin collects the C# code
 * blocks of each Markdown file and processes them in one batch before Shiki
 * runs.
 *
 * @param {import('@glosharp/shiki').TransformerGloSharpOptions} [options]
 */
export function remarkGloSharp(options = {}) {
  return async (tree) => {
    const blocks = []
    visit(tree, 'code', (node) => {
      if (CSHARP.has(node.lang) && node.value) blocks.push(node.value)
    })
    if (blocks.length === 0) return

    const batch = await processGloSharpBlocks(blocks, options)
    for (const [hash, result] of batch) results.set(hash, result)
  }
}

/**
 * The Shiki transformer: looks each code block up in the shared results, swaps
 * in the processed code (markers removed) and adds hovers, errors and
 * completions. Blocks that weren't processed pass through untouched.
 */
export function glosharpTransformer() {
  return transformerGloSharpFromMap(results)
}
