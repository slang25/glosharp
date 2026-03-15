import { processTwohashCode } from '@twohash/shiki'
import { codeToHtml } from 'shiki'

/**
 * Remark plugin that pre-processes C# code blocks containing twohash markers.
 *
 * This replaces fenced ```csharp blocks with rendered HTML that includes
 * hover popups, error annotations, and completion lists.
 *
 * Usage in docusaurus.config.ts:
 *   docs: {
 *     beforeDefaultRemarkPlugins: [remarkTwohash],
 *   }
 */
export function remarkTwohash() {
  return async (tree: any) => {
    const { visit } = await import('unist-util-visit')

    const codeNodes: any[] = []
    visit(tree, 'code', (node: any) => {
      if ((node.lang === 'csharp' || node.lang === 'cs') && node.value) {
        codeNodes.push(node)
      }
    })

    for (const node of codeNodes) {
      const result = await processTwohashCode(node.value)
      if (!result) continue

      // Render with Shiki + twohash transformer
      const { transformerTwohashWithResult } = await import('@twohash/shiki')
      const html = await codeToHtml(node.value, {
        lang: 'csharp',
        themes: { light: 'github-light', dark: 'github-dark' },
        transformers: [transformerTwohashWithResult(result)],
      })

      // Replace the code node with raw HTML
      node.type = 'html'
      node.value = html
      delete node.lang
      delete node.meta
    }
  }
}
