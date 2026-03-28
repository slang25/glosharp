import { describe, it, expect } from 'vitest'
import { pluginTwohash, type PluginTwohashOptions } from '../src/plugin.js'

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

  it('baseStyles includes theme-aware CSS variables', () => {
    const plugin = pluginTwohash()
    // Style values are embedded in baseStyles via CSS custom properties
    expect(plugin.baseStyles).toContain('--twohash-popup-bg')
    expect(plugin.baseStyles).toContain('--twohash-popup-fg')
    expect(plugin.baseStyles).toContain('--twohash-error-underline')
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
    const codeBlock = { code: 'const x = 42;\n//  ^?', language: 'javascript', meta: '' } as any
    // Should not throw or modify
    await plugin.hooks.preprocessCode({ codeBlock })
    expect(codeBlock.code).toBe('const x = 42;\n//  ^?')
  })

  it('preprocessCode processes csharp blocks without markers', async () => {
    const plugin = pluginTwohash()
    const lines = ['var x = 42;']
    const codeBlock = {
      code: 'var x = 42;',
      language: 'csharp',
      meta: '',
      getLines: () => lines.map(text => ({ text })),
      deleteLines: (indices: number[]) => {
        const sorted = [...indices].sort((a, b) => b - a)
        for (const i of sorted) lines.splice(i, 1)
      },
      insertLines: (index: number, newLines: string[]) => {
        lines.splice(index, 0, ...newLines)
      },
    } as any
    await plugin.hooks.preprocessCode({ codeBlock })
    // Code without markers should remain the same
    expect(lines.join('\n')).toBe('var x = 42;')
  })

  it('accepts project option', () => {
    const options: PluginTwohashOptions = {
      project: './MyProject.csproj',
    }
    const plugin = pluginTwohash(options)
    expect(plugin.name).toBe('twohash')
  })

  it('accepts region option', () => {
    const options: PluginTwohashOptions = {
      project: './MyProject.csproj',
      region: 'getting-started',
    }
    const plugin = pluginTwohash(options)
    expect(plugin.name).toBe('twohash')
  })

  it('baseStyles includes completion list styles', () => {
    const plugin = pluginTwohash()
    expect(plugin.baseStyles).toContain('.twohash-completion-list')
    expect(plugin.baseStyles).toContain('.twohash-completion-item')
    expect(plugin.baseStyles).toContain('.twohash-completion-kind')
    expect(plugin.baseStyles).toContain('.twohash-completion-label')
    expect(plugin.baseStyles).toContain('.twohash-completion-detail')
  })
})
