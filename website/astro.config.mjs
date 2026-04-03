// @ts-check
import { defineConfig } from 'astro/config'
import expressiveCode from 'astro-expressive-code'

// https://astro.build/config
export default defineConfig({
  integrations: [
    expressiveCode(),
  ],
  vite: {
    assetsInclude: ['**/*.cs'],
    server: {
      watch: {
        // Ensure .cs example files trigger HMR
      },
    },
    plugins: [{
      name: 'cs-hmr',
      handleHotUpdate({ file, server }) {
        if (file.endsWith('.cs')) {
          server.ws.send({ type: 'full-reload' })
          return []
        }
      },
    }],
  },
})
