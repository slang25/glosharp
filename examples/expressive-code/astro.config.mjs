import { defineConfig } from 'astro/config'
import expressiveCode from 'astro-expressive-code'
import { pluginGloSharp } from '@glosharp/expressive-code'

export default defineConfig({
  integrations: [
    expressiveCode({
      plugins: [pluginGloSharp()],
      themes: ['github-dark', 'github-light'],
    }),
  ],
})
