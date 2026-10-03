import { describe, expect, it } from 'vitest'
import { ExpressiveCodeEngine, PluginStyleSettings, getColorContrast, getLuminance } from '@expressive-code/core'
import { loadShikiTheme } from 'expressive-code'
import { pluginGloSharp } from '../src/plugin.js'

async function themeVars() {
  const engine = new ExpressiveCodeEngine({
    themes: [await loadShikiTheme('github-dark'), await loadShikiTheme('github-light')],
    plugins: [pluginGloSharp()],
  })
  const css = await engine.getThemeStyles()
  // One declaration block per theme, in theme order
  const blocks = [...css.matchAll(/\{([^{}]*--ec-glosharp-[^{}]*)\}/g)].map(m => m[1])
  const parse = (block: string) => Object.fromEntries(
    [...block.matchAll(/--ec-glosharp-([\w-]+):([^;]+)/g)].map(([, k, v]) => [k, v.trim()]),
  )
  return { engine, css, dark: parse(blocks[0]), light: parse(blocks[1]) }
}

describe('theming', () => {
  it('registers glosharp style settings with Expressive Code', () => {
    const plugin = pluginGloSharp()
    expect(plugin.styleSettings).toBeInstanceOf(PluginStyleSettings)
  })

  it('derives popup colours from each EC theme, so light themes get light popups', async () => {
    const { dark, light } = await themeVars()
    const popupBg = Object.keys(dark).find(k => k.startsWith('popupB') && !k.includes('rd'))!
    const popupFg = Object.keys(dark).find(k => k.startsWith('popupF'))!
    expect(getLuminance(dark[popupBg])).toBeLessThan(0.2)
    expect(getLuminance(light[popupBg])).toBeGreaterThan(0.8)
    // Readable text in both themes
    expect(getColorContrast(dark[popupFg], dark[popupBg])).toBeGreaterThanOrEqual(7)
    expect(getColorContrast(light[popupFg], light[popupBg])).toBeGreaterThanOrEqual(7)
  })

  it('keeps every popup syntax colour and muted text readable on the popup background', async () => {
    const { dark, light } = await themeVars()
    for (const vars of [dark, light]) {
      const bg = vars[Object.keys(vars).find(k => k.startsWith('popupB') && !k.includes('rd'))!]
      const textish = Object.entries(vars).filter(([k]) => k.startsWith('syntax') || k.startsWith('popupMuted'))
      expect(textish.length).toBeGreaterThanOrEqual(6)
      for (const [name, color] of textish) {
        expect(getColorContrast(color, bg), `${name} ${color} on ${bg}`).toBeGreaterThanOrEqual(4.5)
      }
    }
  })

  it('uses EC theme variables instead of [data-theme] selectors', async () => {
    const { engine } = await themeVars()
    const base = await engine.getBaseStyles()
    expect(base).not.toContain('data-theme')
    expect(base).toMatch(/\.glosharp-popup-container[^{]*\{[^}]*background:var\(--ec-glosharp-/)
    expect(base).toMatch(/\.glosharp-completion-kind\{[^}]*color:var\(--ec-glosharp-/)
    expect(base).toMatch(/\.glosharp-static-container[^{]*\{[^}]*background:var\(--ec-glosharp-/)
  })

  it('draws error squiggles with a valid wavy text-decoration (not an invalid wavy border)', async () => {
    const { engine } = await themeVars()
    const base = await engine.getBaseStyles()
    expect(base).not.toMatch(/border[^;]*wavy/)
    expect(base).toMatch(/\.glosharp-error-underline\{[^}]*text-decoration-style:wavy/)
  })

  it('lets users override colours via styleOverrides.glosharp', async () => {
    const engine = new ExpressiveCodeEngine({
      themes: [await loadShikiTheme('github-dark')],
      plugins: [pluginGloSharp()],
      styleOverrides: { glosharp: { popupBackground: '#123456' } },
    })
    expect(await engine.getThemeStyles()).toMatch(/--ec-glosharp-popupBg:#123456/)
  })
})
