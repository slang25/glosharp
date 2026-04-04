import { describe, it, expect } from 'vitest'
import { pluginGloSharp, type PluginGloSharpOptions } from '../src/plugin.js'

describe('pluginGloSharp', () => {
  it('returns a plugin with correct name', () => {
    const plugin = pluginGloSharp()
    expect(plugin.name).toBe('glosharp')
  })

  it('has all required hooks', () => {
    const plugin = pluginGloSharp()
    expect(plugin.hooks).toBeDefined()
    expect(plugin.hooks.preprocessCode).toBeDefined()
    expect(plugin.hooks.annotateCode).toBeDefined()
    expect(plugin.hooks.postprocessRenderedBlock).toBeDefined()
  })

  it('has baseStyles with CSS', () => {
    const plugin = pluginGloSharp()
    expect(plugin.baseStyles).toContain('.glosharp-hover')
    expect(plugin.baseStyles).toContain('.glosharp-popup')
    // anchor-name and position-anchor are set as inline styles on individual elements,
    // not in the base stylesheet — verify core layout rules instead
    expect(plugin.baseStyles).toContain('position: fixed')
    expect(plugin.baseStyles).toContain('position-area: top')
    expect(plugin.baseStyles).toContain('position-try-fallbacks: flip-block')
    expect(plugin.baseStyles).toContain('max-height: 40vh')
    expect(plugin.baseStyles).toContain('overflow-y: auto')
  })

  it('does not expose styleSettings (styles are in baseStyles)', () => {
    const plugin = pluginGloSharp()
    expect((plugin as any).styleSettings).toBeUndefined()
  })

  it('baseStyles includes part kind color classes', () => {
    const plugin = pluginGloSharp()
    expect(plugin.baseStyles).toContain('.glosharp-keyword')
    expect(plugin.baseStyles).toContain('.glosharp-className')
    expect(plugin.baseStyles).toContain('.glosharp-localName')
    expect(plugin.baseStyles).toContain('.glosharp-methodName')
  })

  it('preprocessCode skips non-csharp blocks', async () => {
    const plugin = pluginGloSharp()
    const codeBlock = { code: 'const x = 42;\n//  ^?', language: 'javascript', meta: '' } as any
    // Should not throw or modify
    await plugin.hooks.preprocessCode({ codeBlock })
    expect(codeBlock.code).toBe('const x = 42;\n//  ^?')
  })

  it('preprocessCode processes csharp blocks without markers', async () => {
    const plugin = pluginGloSharp()
    const lines = ['var x = 42;']
    const codeBlock = {
      get code() { return lines.join('\n') },
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
    expect(codeBlock.code).toBe('var x = 42;')
  })

  it('accepts project option', () => {
    const options: PluginGloSharpOptions = {
      project: './MyProject.csproj',
    }
    const plugin = pluginGloSharp(options)
    expect(plugin.name).toBe('glosharp')
  })

  it('accepts region option', () => {
    const options: PluginGloSharpOptions = {
      project: './MyProject.csproj',
      region: 'getting-started',
    }
    const plugin = pluginGloSharp(options)
    expect(plugin.name).toBe('glosharp')
  })

  it('baseStyles includes completion list styles', () => {
    const plugin = pluginGloSharp()
    expect(plugin.baseStyles).toContain('.glosharp-completion-list')
    expect(plugin.baseStyles).toContain('.glosharp-completion-item')
    expect(plugin.baseStyles).toContain('.glosharp-completion-kind')
    expect(plugin.baseStyles).toContain('.glosharp-completion-label')
    expect(plugin.baseStyles).toContain('.glosharp-completion-detail')
  })
})
