import { defineConfig } from 'astro/config'
import { remarkTwohash, twohashTransformer } from './src/remark-twohash.mjs'

export default defineConfig({
  markdown: {
    remarkPlugins: [remarkTwohash],
    shikiConfig: {
      themes: {
        light: 'github-light',
        dark: 'github-dark',
      },
      transformers: [twohashTransformer()],
    },
  },
})
