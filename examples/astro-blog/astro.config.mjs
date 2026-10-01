import { defineConfig } from 'astro/config'
import { remarkGloSharp, glosharpTransformer } from './src/remark-glosharp.mjs'

const glosharpOptions = {
  // Path to the glosharp CLI. Leave unset to find `glosharp` on PATH.
  executable: process.env.GLOSHARP_EXECUTABLE,
}

export default defineConfig({
  markdown: {
    remarkPlugins: [[remarkGloSharp, glosharpOptions]],
    shikiConfig: {
      themes: {
        light: 'github-light',
        dark: 'github-dark',
      },
      transformers: [glosharpTransformer()],
    },
  },
})
