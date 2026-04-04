import { processGloSharpCode, type TransformerGloSharpOptions } from '@glosharp/shiki'
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

    for (const node of codeNodes) {
      const result = await processGloSharpCode(node.value, options)
      if (!result) continue

      // Render with Shiki + glosharp transformer
      const { transformerGloSharpWithResult } = await import('@glosharp/shiki')
      const html = await codeToHtml(result.code, {
        lang: 'csharp',
        themes: { light: 'github-light', dark: 'github-dark' },
        transformers: [transformerGloSharpWithResult(result)],
      })

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
