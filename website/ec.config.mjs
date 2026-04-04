import { pluginGloSharp } from '@glosharp/expressive-code'

/** @type {import('astro-expressive-code').AstroExpressiveCodeOptions} */
export default {
  plugins: [pluginGloSharp()],
  themes: ['github-dark'],
  styleOverrides: {
    codeFontFamily: "'JetBrains Mono', monospace",
  },
}
