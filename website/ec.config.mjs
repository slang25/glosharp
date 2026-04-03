import { pluginTwohash } from '@twohash/expressive-code'

/** @type {import('astro-expressive-code').AstroExpressiveCodeOptions} */
export default {
  plugins: [pluginTwohash()],
  themes: ['github-dark'],
  styleOverrides: {
    codeFontFamily: "'JetBrains Mono', monospace",
  },
}
