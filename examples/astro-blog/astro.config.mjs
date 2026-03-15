import { defineConfig } from 'astro/config'
import { processTwohashCode, transformerTwohashWithResult } from '@twohash/shiki'

export default defineConfig({
  markdown: {
    shikiConfig: {
      themes: {
        light: 'github-light',
        dark: 'github-dark',
      },
      transformers: [
        // Custom transformer that processes twohash markers on-the-fly.
        // For better performance with many code blocks, consider using
        // processTwohashBlocks() + transformerTwohashFromMap() instead.
        {
          name: 'twohash-lazy',
          async preprocess(code) {
            if (this.options.lang !== 'csharp' && this.options.lang !== 'cs') return
            const result = await processTwohashCode(code)
            if (result) {
              this.__twohashResult = result
              return result.code
            }
          },
          root(hast) {
            if (!this.__twohashResult) return
            const inner = transformerTwohashWithResult(this.__twohashResult)
            inner.root.call(this, hast)
            this.__twohashResult = undefined
          },
        },
      ],
    },
  },
})
