import { describe, expect, it } from 'vitest'
import { ExpressiveCodeBlock, type ExpressiveCodePlugin } from '@expressive-code/core'
import type { ExpressiveCodeConfig } from 'expressive-code'
import { pluginGloSharp, shouldProcessBlock, type PluginGloSharpOptions } from '../src/index.js'

describe('pluginGloSharp', () => {
  it('is a typed Expressive Code plugin usable in a type-checked EC config', () => {
    // Compile-time check (tsc -p tsconfig.test.json): `astro check` on a typed
    // ec.config.mjs used to reject the hand-typed hooks (U-astro F21)
    const plugin: ExpressiveCodePlugin = pluginGloSharp()
    const config: ExpressiveCodeConfig = { plugins: [pluginGloSharp({ cacheDir: '.cache/glosharp' })] }
    expect(plugin.name).toBe('glosharp')
    expect(config.plugins).toHaveLength(1)
    expect(plugin.hooks?.preprocessCode).toBeTypeOf('function')
    expect(plugin.hooks?.annotateCode).toBeTypeOf('function')
  })

  it('accepts every documented option', () => {
    const options: Required<PluginGloSharpOptions> = {
      executable: 'glosharp',
      framework: 'net8.0',
      cacheDir: '.cache/glosharp',
      configFile: 'glosharp.json',
      complog: 'build.complog',
      complogProject: 'App',
      project: './Demo.csproj',
      region: 'demo',
      explicitTrigger: true,
      onCliError: 'warn',
      failOnErrors: true,
    }
    expect(pluginGloSharp(options).name).toBe('glosharp')
  })
})

describe('shouldProcessBlock', () => {
  const block = (language: string, meta = '') => new ExpressiveCodeBlock({ code: 'x', language, meta })

  it('processes C# blocks by default (spec: all C# blocks, with or without markers)', () => {
    expect(shouldProcessBlock(block('csharp'))).toBe(true)
    expect(shouldProcessBlock(block('cs'))).toBe(true)
    expect(shouldProcessBlock(block('c#'))).toBe(true)
    expect(shouldProcessBlock(block('js'))).toBe(false)
    expect(shouldProcessBlock(block('fsharp', 'glosharp'))).toBe(false)
  })

  it('honours the no-glosharp / glosharp=false opt-outs', () => {
    expect(shouldProcessBlock(block('cs', 'no-glosharp'))).toBe(false)
    expect(shouldProcessBlock(block('cs', 'title="a.cs" glosharp=false'))).toBe(false)
    expect(shouldProcessBlock(block('cs', 'glosharp no-glosharp'), true)).toBe(false)
  })

  it('requires the glosharp flag when explicitTrigger is enabled', () => {
    expect(shouldProcessBlock(block('cs'), true)).toBe(false)
    expect(shouldProcessBlock(block('cs', 'glosharp'), true)).toBe(true)
    expect(shouldProcessBlock(block('cs', '{1-3} glosharp title="x"'), true)).toBe(true)
  })
})
