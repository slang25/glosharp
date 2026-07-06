import { describe, it, expect } from 'vitest'
import { chmodSync } from 'node:fs'
import { join } from 'node:path'
import { ExpressiveCodeEngine } from '@expressive-code/core'
import { toHtml } from '@expressive-code/core/hast'
import { pluginGloSharp } from '../src/plugin.js'

// End-to-end renders through the real EC engine, with GloSharp results served
// by a canned stub executable. EC core validates that every annotation's
// render returns exactly as many nodes as it received — these tests pin the
// two annotation shapes that used to violate that contract and crash.

const STUB = join(import.meta.dirname!, 'glosharp-stub.mjs')
chmodSync(STUB, 0o755)

function createEngine() {
  return new ExpressiveCodeEngine({
    plugins: [pluginGloSharp({ executable: STUB }) as never],
  })
}

describe('engine rendering', () => {
  it('renders completion lists without violating EC render validation', async () => {
    const engine = createEngine()
    const { renderedGroupAst } = await engine.render({
      code: 'Console.\n//      ^|',
      language: 'csharp',
    })
    const html = toHtml(renderedGroupAst)
    expect(html).toContain('glosharp-completion-list')
    expect(html).toContain('WriteLine')
    expect(html).toContain('glosharp-completion-kind-Method')
  })

  it('renders multi-line error messages without violating EC render validation', async () => {
    const engine = createEngine()
    const { renderedGroupAst } = await engine.render({
      code: 'int total = "hello" +\n    " world" +\n    "!";',
      language: 'csharp',
    })
    const html = toHtml(renderedGroupAst)
    expect(html).toContain('glosharp-error-message')
    expect(html).toContain('CS0029')
    // Underlines on every affected line of the span
    expect(html.match(/glosharp-error-underline/g)!.length).toBeGreaterThanOrEqual(3)
    // The message box appears after the last affected line's underline
    expect(html.lastIndexOf('glosharp-error-message')).toBeGreaterThan(html.lastIndexOf('glosharp-error-underline'))
  })
})
