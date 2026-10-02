import { afterEach, describe, expect, it } from 'vitest'
import { ExpressiveCode, loadShikiTheme } from 'expressive-code'
import { pluginCollapsibleSections } from '@expressive-code/plugin-collapsible-sections'
import { pluginLineNumbers } from '@expressive-code/plugin-line-numbers'
import { selectAll, toHtml, type Element } from '@expressive-code/core/hast'
import { pluginGloSharp } from '../src/plugin.js'
import { alignLines, syncLines } from '../src/line-sync.js'
import { STUB_EXECUTABLE, classes, lineCodeText, resetStub, useStubResults } from './helpers.js'

afterEach(resetStub)

// Source as written in markdown: line 2 is a `^?` marker and line 4 an
// `@highlight` directive, both removed by glosharp.
const SOURCE = [
  'using System;',            // 1
  '//    ^?',                 // 2 (marker, removed)
  'var total = 1 + 2;',       // 3
  '// @highlight',            // 4 (directive, removed)
  'Console.WriteLine(total);', // 5
  'var unused = 0;',          // 6
].join('\n')

const RESULT = {
  code: 'using System;\nvar total = 1 + 2;\nConsole.WriteLine(total);\nvar unused = 0;\n',
  hovers: [{
    line: 0, character: 6, length: 6, text: 'System', parts: [{ kind: 'namespaceName', text: 'System' }],
    docs: null, symbolKind: 'Namespace', targetText: 'System', persistent: true,
  }, {
    line: 1, character: 4, length: 5, text: 'total', parts: [{ kind: 'localName', text: 'total' }],
    docs: null, symbolKind: 'Local', targetText: 'total',
  }],
  highlights: [{ line: 2, character: 0, length: 25, kind: 'highlight' }],
  errors: [{ line: 3, character: 4, length: 6, code: 'CS0219', message: 'assigned but never used', severity: 'warning', expected: false }],
}

async function renderWithEc(meta: string) {
  useStubResults({ 'Console.WriteLine(total)': RESULT })
  const ec = new ExpressiveCode({
    themes: [await loadShikiTheme('github-dark')],
    plugins: [pluginGloSharp({ executable: STUB_EXECUTABLE }), pluginCollapsibleSections(), pluginLineNumbers()],
  })
  const { renderedGroupAst } = await ec.render({ code: SOURCE, language: 'csharp', meta })
  return { ast: renderedGroupAst, html: toHtml(renderedGroupAst) }
}

const ecLines = (ast: Element) => selectAll('.ec-line', ast) as Element[]

describe('composition with core Expressive Code features', () => {
  it('keeps `{n}` line markers on the source lines they refer to', async () => {
    const { ast } = await renderWithEc('{3} ins={5} del={6}')
    const lines = ecLines(ast).filter(l => !classes(l).includes('collapsible'))
    expect(lines.map(lineCodeText)).toEqual(['using System;', 'var total = 1 + 2;', 'Console.WriteLine(total);', 'var unused = 0;'])
    expect(classes(lines[1])).toEqual(expect.arrayContaining(['highlight', 'mark']))
    expect(classes(lines[2])).toEqual(expect.arrayContaining(['highlight', 'ins', 'glosharp-highlight']))
    expect(classes(lines[3])).toEqual(expect.arrayContaining(['highlight', 'del']))
    expect(classes(lines[0])).not.toContain('mark')
  })

  it('keeps collapse={…} sections', async () => {
    const { ast } = await renderWithEc('collapse={5-6}')
    const details = selectAll('details', ast)
    expect(details).toHaveLength(1)
    // The two collapsed lines, one of which carries an error message block
    const collapsed = (selectAll('.ec-line', details[0]) as Element[]).map(lineCodeText).filter(t => !t.includes('collapsed'))
    expect(collapsed).toEqual(['Console.WriteLine(total);', 'var unused = 0;'])
    expect(selectAll('.glosharp-error-message', details[0])).toHaveLength(1)
  })

  it('composes with frames titles, line numbers and inline text markers', async () => {
    const { ast, html } = await renderWithEc('title="Program.cs" showLineNumbers "total"')
    expect(html).toContain('Program.cs')
    // Line numbers count rendered lines
    expect(selectAll('.ec-line .gutter .ln', ast).map(n => (n as Element).children.map(c => (c as { value?: string }).value).join(''))).toEqual(['1', '2', '3', '4'])
    // The text marker and the hover both apply to `total`
    expect(selectAll('mark', ast).length).toBeGreaterThan(0)
    expect(selectAll('.glosharp-hover', ast)).toHaveLength(1)
    // Static ^? result and error message render as blocks after their lines
    expect(selectAll('.glosharp-line > .glosharp-line-extras > .glosharp-static', ast)).toHaveLength(1)
    expect(selectAll('.glosharp-line > .glosharp-line-extras > .glosharp-error-message', ast)).toHaveLength(1)
  })

  it('copies the cleaned code (markers removed)', async () => {
    const { ast } = await renderWithEc('')
    const copy = selectAll('.copy button', ast)[0] as Element | undefined
    expect(copy?.properties.dataCode).toBe('using System;\x7Fvar total = 1 + 2;\x7FConsole.WriteLine(total);\x7Fvar unused = 0;')
  })
})

describe('line synchronisation', () => {
  function fakeBlock(texts: string[]) {
    const lines = texts.map(text => ({
      text,
      editText(_s: number | undefined, _e: number | undefined, t: string) { this.text = t; return t },
    }))
    return {
      lines,
      getLines: () => lines,
      deleteLines: (indices: number[]) => { for (const i of [...indices].sort((a, b) => b - a)) lines.splice(i, 1) },
      insertLines: (index: number, newTexts: string[]) => { lines.splice(index, 0, ...newTexts.map(t => fakeBlock([t]).lines[0])) },
    }
  }

  it('keeps the original line objects for surviving lines', () => {
    const block = fakeBlock(SOURCE.split('\n'))
    const before = [...block.lines]
    syncLines(block, RESULT.code.replace(/\n$/, '').split('\n'))
    expect(block.lines.map(l => l.text)).toEqual(['using System;', 'var total = 1 + 2;', 'Console.WriteLine(total);', 'var unused = 0;'])
    expect(block.lines[0]).toBe(before[0])
    expect(block.lines[1]).toBe(before[2])
    expect(block.lines[2]).toBe(before[4])
    expect(block.lines[3]).toBe(before[5])
  })

  it('handles cut sections, duplicate lines, CRLF and inserted lines', () => {
    const block = fakeBlock(['class A {', '}', '// ---cut---', 'class B {\r', '}\r'])
    const before = [...block.lines]
    syncLines(block, ['class B {', '}', 'extra'])
    expect(block.lines.map(l => l.text)).toEqual(['class B {', '}', 'extra'])
    expect(block.lines[0]).toBe(before[3])
    expect(block.lines[1]).toBe(before[4])
  })

  it('maps rendered lines back to source lines', () => {
    expect(alignLines(SOURCE.split('\n'), RESULT.code.replace(/\n$/, '').split('\n'))).toEqual([0, 2, 4, 5])
  })
})
