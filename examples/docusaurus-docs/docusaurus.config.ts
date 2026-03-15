import type { Config } from '@docusaurus/types'
import type * as Preset from '@docusaurus/preset-classic'

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
        },
        blog: false,
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
