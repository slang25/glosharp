import type { Config } from '@docusaurus/types'
import type * as Preset from '@docusaurus/preset-classic'
import { remarkTwohash } from './src/plugins/twohash-plugin'

const config: Config = {
  title: 'My C# Library',
  tagline: 'Documentation with interactive code samples',
  url: 'https://example.com',
  baseUrl: '/',

  presets: [
    [
      'classic',
      {
        docs: {
          routeBasePath: '/',
          sidebarPath: './sidebars.ts',
          beforeDefaultRemarkPlugins: [remarkTwohash],
        },
        blog: false,
        theme: {
          customCss: './src/css/custom.css',
        },
      } satisfies Preset.Options,
    ],
  ],

  themeConfig: {
    navbar: {
      title: 'My C# Library',
    },
  } satisfies Preset.ThemeConfig,
}

export default config
