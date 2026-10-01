import { defineConfig } from 'astro/config'
import expressiveCode from 'astro-expressive-code'
import { pluginGloSharp } from '@glosharp/expressive-code'

export default defineConfig({
  integrations: [
    expressiveCode({
      plugins: [
        pluginGloSharp({
          // Path to the glosharp CLI. Leave unset to find `glosharp` on PATH.
          executable: process.env.GLOSHARP_EXECUTABLE,
        }),
      ],
      themes: ['github-dark', 'github-light'],
    }),
  ],
})
