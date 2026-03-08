import { describe, it, expect } from 'vitest'
import { pluginTwohash } from '../src/plugin.js'

describe('pluginTwohash', () => {
  it('returns a plugin with correct name', () => {
    const plugin = pluginTwohash()
    expect(plugin.name).toBe('twohash')
  })

  it('has all required hooks', () => {
    const plugin = pluginTwohash()
    expect(plugin.hooks).toBeDefined()
    expect(plugin.hooks.preprocessCode).toBeDefined()
    expect(plugin.hooks.annotateCode).toBeDefined()
    expect(plugin.hooks.postprocessRenderedBlock).toBeDefined()
  })

  it('has baseStyles with CSS', () => {
    const plugin = pluginTwohash()
    expect(plugin.baseStyles).toContain('.twohash-hover')
    expect(plugin.baseStyles).toContain('.twohash-popup')
    // anchor-name and position-anchor are set as inline styles on individual elements,
    // not in the base stylesheet — verify core layout rules instead
    expect(plugin.baseStyles).toContain('position: fixed')
    expect(plugin.baseStyles).toContain('position-area: top')
  })

  it('has theme-aware styleSettings', () => {
    const plugin = pluginTwohash()
    expect(plugin.styleSettings).toBeDefined()
    expect(plugin.styleSettings.popupBackground).toHaveProperty('dark')
    expect(plugin.styleSettings.popupBackground).toHaveProperty('light')
    expect(plugin.styleSettings.errorUnderline).toHaveProperty('dark')
    expect(plugin.styleSettings.errorUnderline).toHaveProperty('light')
  })

  it('baseStyles includes part kind color classes', () => {
    const plugin = pluginTwohash()
    expect(plugin.baseStyles).toContain('.twohash-keyword')
    expect(plugin.baseStyles).toContain('.twohash-className')
    expect(plugin.baseStyles).toContain('.twohash-localName')
    expect(plugin.baseStyles).toContain('.twohash-methodName')
  })

  it('preprocessCode skips non-csharp blocks', async () => {
    const plugin = pluginTwohash()
    const codeBlock = { code: 'const x = 42;\n//  ^?', language: 'javascript', meta: '' }
    // Should not throw or modify
    await plugin.hooks.preprocessCode({ codeBlock })
    expect(codeBlock.code).toBe('const x = 42;\n//  ^?')
  })

  it('preprocessCode skips csharp blocks without markers', async () => {
    const plugin = pluginTwohash()
    const codeBlock = { code: 'var x = 42;', language: 'csharp', meta: '' }
    await plugin.hooks.preprocessCode({ codeBlock })
    expect(codeBlock.code).toBe('var x = 42;')
  })
})
