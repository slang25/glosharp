import { processGloSharpBlocks, transformerGloSharpFromMap, type TransformerGloSharpOptions } from '@glosharp/shiki'
import { codeToHtml } from 'shiki'

/**
 * Remark plugin that pre-processes C# code blocks containing glosharp markers.
 *
 * This replaces fenced ```csharp blocks with rendered HTML that includes
 * hover popups, error annotations, and completion lists.
 *
 * Usage in docusaurus.config.ts:
 *   docs: {
 *     beforeDefaultRemarkPlugins: [[remarkGloSharp, { executable: '...' }]],
 *   }
 */
export function remarkGloSharp(options: TransformerGloSharpOptions = {}) {
  return async (tree: any) => {
    const { visit } = await import('unist-util-visit')

    const codeNodes: any[] = []
    visit(tree, 'code', (node: any) => {
      if ((node.lang === 'csharp' || node.lang === 'cs') && node.value) {
        codeNodes.push(node)
      }
    })

    if (codeNodes.length === 0) return

    // One batch call shares a single glosharp instance -- and its result cache --
    // across every block and processes them concurrently, rather than spawning a
    // fresh instance per block and awaiting each in turn.
    const resultMap = await processGloSharpBlocks(
      codeNodes.map((node: any) => node.value),
      options,
    )

    // The transformer keeps per-render state, so each concurrent codeToHtml call
    // needs its own instance reading from the shared result map.
    const htmls = await Promise.all(
      codeNodes.map((node: any) =>
        codeToHtml(node.value, {
          lang: 'csharp',
          themes: { light: 'github-light', dark: 'github-dark' },
          transformers: [transformerGloSharpFromMap(resultMap)],
        }),
      ),
    )

    for (const [i, node] of codeNodes.entries()) {
      const html = htmls[i]

      // Replace the code node with an MDX JSX element (dangerouslySetInnerHTML)
      // so it works with Docusaurus's MDX pipeline without needing rehype-raw
      node.type = 'mdxJsxFlowElement'
      node.name = 'div'
      node.attributes = [
        {
          type: 'mdxJsxAttribute',
          name: 'dangerouslySetInnerHTML',
          value: {
            type: 'mdxJsxAttributeValueExpression',
            value: `{ __html: ${JSON.stringify(html)} }`,
            data: {
              estree: {
                type: 'Program',
                body: [
                  {
                    type: 'ExpressionStatement',
                    expression: {
                      type: 'ObjectExpression',
                      properties: [
                        {
                          type: 'Property',
                          method: false,
                          shorthand: false,
                          computed: false,
                          key: { type: 'Identifier', name: '__html' },
                          value: {
                            type: 'Literal',
                            value: html,
                            raw: JSON.stringify(html),
                          },
                          kind: 'init',
                        },
                      ],
                    },
                  },
                ],
                sourceType: 'module',
              },
            },
          },
        },
      ]
      node.children = []
      node.data = undefined
      delete node.lang
      delete node.meta
      delete node.value
    }
  }
}
