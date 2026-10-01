import { pluginGloSharp } from '@glosharp/expressive-code'

/** @type {import('astro-expressive-code').AstroExpressiveCodeOptions} */
export default {
  plugins: [
    pluginGloSharp({
      // Path to the glosharp CLI. Leave unset to find `glosharp` on PATH.
      executable: process.env.GLOSHARP_EXECUTABLE,
    }),
  ],
  themes: ['github-dark'],
  styleOverrides: {
    codeFontFamily: "'JetBrains Mono', monospace",
  },
}
