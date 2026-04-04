import { defineConfig } from 'astro/config'
import { remarkGloSharp, glosharpTransformer } from './src/remark-glosharp.mjs'

export default defineConfig({
  markdown: {
    remarkPlugins: [remarkGloSharp],
    shikiConfig: {
      themes: {
        light: 'github-light',
        dark: 'github-dark',
      },
      transformers: [glosharpTransformer()],
    },
  },
})
