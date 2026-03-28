import { defineConfig } from 'astro/config'
import expressiveCode from 'astro-expressive-code'
import { pluginTwohash } from '@twohash/expressive-code'

export default defineConfig({
  integrations: [
    expressiveCode({
      plugins: [pluginTwohash()],
      themes: ['github-dark', 'github-light'],
    }),
  ],
})
