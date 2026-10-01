import { afterEach, describe, expect, it } from 'vitest'
import type { Element, Nodes } from '@expressive-code/core/hast'
import { classes, createEngine, lineCodeText, resetStub, select, textOf, useStubResults, walk } from './helpers.js'

afterEach(resetStub)

const hover = (line: number, character: number, length: number, name: string, extra: object = {}) => ({
  line, character, length,
  text: name,
  parts: [{ kind: 'localName', text: name }],
  docs: null,
  symbolKind: 'Local',
  targetText: name,
  ...extra,
})

const error = (line: number, character: number, length: number, extra: object = {}) => ({
  line, character, length,
  code: 'CS0029',
  message: "Cannot implicitly convert type 'System.DateTime' to 'int'",
  severity: 'error',
  expected: false,
  ...extra,
})

/** Asserts the tree is HAST that strict consumers (Astro 7.3's Sätteri) accept. */
function expectWellFormed(ast: Nodes) {
  const problems: string[] = []
  walk(ast, (node: Nodes, ancestors) => {
    if (node.type === 'root' && ancestors.length > 0) {
      const parent = ancestors[ancestors.length - 1] as Element
      problems.push(`nested root inside <${parent.tagName ?? parent.type} class="${parent.type === 'element' ? classes(parent).join(' ') : ''}">`)
    }
    if (node.type === 'element') {
      if (typeof node.tagName !== 'string' || !Array.isArray(node.children)) problems.push(`malformed element ${JSON.stringify(node).slice(0, 80)}`)
    } else if (node.type === 'text') {
      if (typeof node.value !== 'string') problems.push('text node without string value')
    } else if (!['root', 'comment', 'doctype'].includes((node as { type: string }).type)) {
      problems.push(`unexpected node type ${(node as { type: string }).type}`)
    }
  })
  expect(problems).toEqual([])
}

describe('HAST well-formedness', () => {
  it('emits no nested root nodes when an error range covers several hovered tokens', async () => {
    // U-astro F2: `int y = DateTime.Now;` crashed `astro build` on Astro 7.3
    useStubResults({
      'DateTime.Now': {
        hovers: [hover(0, 4, 1, 'y'), hover(0, 8, 8, 'DateTime'), hover(0, 17, 3, 'Now')],
        errors: [error(0, 8, 12)],
        meta: { compileSucceeded: false },
      },
    })
    const { render } = createEngine()
    const { ast } = await render('int y = DateTime.Now;')
    expectWellFormed(ast)

    // The underline wraps both hover tokens, which keep their popups
    const underline = select(ast, '.glosharp-error-underline')
    expect(underline).toHaveLength(1)
    expect(select(underline[0], '.glosharp-hover')).toHaveLength(2)
  })

  it('emits no nested root nodes when a hover range covers several error ranges', async () => {
    useStubResults({
      'record P': {
        code: 'record P(int X);\nvar p = new P(1);\n',
        hovers: [hover(1, 4, 1, 'p'), hover(1, 8, 9, 'new P(1)')],
        errors: [error(1, 12, 1, { code: 'CS8803', message: 'Top-level statements must precede namespace and type declarations.' }), error(1, 14, 1)],
        meta: { compileSucceeded: false },
      },
    })
    const { render } = createEngine()
    const { ast } = await render('record P(int X);\nvar p = new P(1);')
    expectWellFormed(ast)
  })
})

describe('error rendering', () => {
  it('renders the message as a block after the line without splitting the code line', async () => {
    // R-node #7 / U-astro F7
    useStubResults({
      'missingVar': {
        errors: [error(0, 18, 10, { code: 'CS0103', message: "The name 'missingVar' does not exist in the current context" })],
      },
    })
    const { render } = createEngine()
    const { ast } = await render('Console.WriteLine(missingVar);')

    const lines = select(ast, '.ec-line')
    expect(lines).toHaveLength(1)
    // The whole statement is still one uninterrupted code line
    expect(lineCodeText(lines[0])).toBe('Console.WriteLine(missingVar);')
    // ...and the message is neither inside the line nor inside the underline
    expect(select(lines[0], '.glosharp-error-message')).toHaveLength(0)
    const message = select(ast, '.glosharp-line > .glosharp-line-extras > .glosharp-error-message')
    expect(message).toHaveLength(1)
    expect(textOf(message[0])).toContain("CS0103: The name 'missingVar' does not exist")
  })

  it('shows expected (@errors) diagnostics, marked as expected', async () => {
    // R-node #5 / U-astro F6
    useStubResults({
      'three': {
        code: 'int count = "three";\n',
        errors: [error(0, 12, 7, { message: "Cannot implicitly convert type 'string' to 'int'", expected: true })],
      },
    })
    const { render, warnings } = createEngine()
    const { ast } = await render('// @errors: CS0029\nint count = "three";')
    const underline = select(ast, '.glosharp-error-underline')
    expect(underline).toHaveLength(1)
    expect(classes(underline[0])).toContain('glosharp-error-expected')
    const message = select(ast, '.glosharp-error-message.glosharp-error-expected')
    expect(message).toHaveLength(1)
    expect(textOf(message[0])).toContain('CS0029')
    // Expected errors are not build problems
    expect(warnings).toEqual([])
  })

  it('underlines every line of a multi-line diagnostic and shows the message once after the last', async () => {
    const { render } = createEngine()
    const { ast } = await render('int total = "hello" +\n    " world" +\n    "!";')
    const lines = select(ast, '.ec-line')
    expect(lines).toHaveLength(3)
    for (const line of lines) {
      expect(select(line, '.glosharp-error-underline').length).toBeGreaterThan(0)
    }
    const messages = select(ast, '.glosharp-error-message')
    expect(messages).toHaveLength(1)
    // Attached to the third line
    const wrapper = select(ast, '.glosharp-line')
    expect(wrapper).toHaveLength(1)
    expect(lineCodeText(select(wrapper[0], '.ec-line')[0])).toBe('    "!";')
  })

  it('underlines a zero-width diagnostic on an adjacent character', async () => {
    useStubResults({
      'var x = 1': { errors: [error(0, 9, 0, { code: 'CS1002', message: '; expected' })] },
    })
    const { render } = createEngine()
    const { ast } = await render('var x = 1')
    const underline = select(ast, '.glosharp-error-underline')
    expect(underline).toHaveLength(1)
    expect(textOf(underline[0])).toBe('1')
  })

  it('links CS error codes to the docs', async () => {
    useStubResults({ 'missingVar': { errors: [error(0, 0, 10, { code: 'CS0103' })] } })
    const { render } = createEngine()
    const { ast } = await render('missingVar;')
    const link = select(ast, 'a.glosharp-error-code')
    expect(link).toHaveLength(1)
    expect(link[0].properties.href).toBe('https://msdn.microsoft.com/query/roslyn.query?appId=roslyn&k=k(CS0103)')
  })
})

describe('block content after lines', () => {
  it('renders a completion list below the line without breaking EC render validation', async () => {
    const { render } = createEngine()
    const { ast, html } = await render('Console.\n//      ^|')
    expect(select(ast, '.ec-line')).toHaveLength(1)
    const items = select(ast, '.glosharp-line-extras .glosharp-completion-list > li')
    expect(items.map(textOf)).toEqual(['MethodWriteLinevoid Console.WriteLine(string?)', 'MethodReadLinestring? Console.ReadLine()'])
    expect(html).toContain('glosharp-completion-kind-Method')
  })

  it('collects static ^? results, errors and tags for one line into a single wrapper', async () => {
    useStubResults({
      'var x = 42': {
        code: 'var x = 42;\n',
        hovers: [hover(0, 4, 1, 'x', { persistent: true })],
        errors: [error(0, 8, 2, { severity: 'warning', code: 'CS0219', message: 'The variable is assigned but its value is never used' })],
        tags: [{ name: 'log', text: 'hello', line: 0 }],
      },
    })
    const { render } = createEngine()
    const { ast } = await render('var x = 42;\n//  ^?\n// @log: hello')
    const wrappers = select(ast, '.glosharp-line')
    expect(wrappers).toHaveLength(1)
    const extras = select(wrappers[0], '.glosharp-line-extras > *').map(e => classes(e)[0])
    expect(extras).toEqual(['glosharp-static', 'glosharp-error-message', 'glosharp-tag'])
    // Static results are visible without interaction and not focus targets
    expect(select(ast, '.glosharp-hover')).toHaveLength(0)
    expect(select(ast, '.glosharp-static-container')).toHaveLength(1)
  })

  it('applies highlight, diff and focus as classes on the .ec-line itself', async () => {
    useStubResults({
      'a();': {
        code: 'a();\nb();\nc();\n',
        highlights: [
          { line: 0, character: 0, length: 4, kind: 'highlight' },
          { line: 1, character: 0, length: 4, kind: 'add' },
          { line: 1, character: 0, length: 4, kind: 'focus' },
          { line: 2, character: 0, length: 4, kind: 'remove' },
        ],
      },
    })
    const { render } = createEngine()
    const { ast } = await render('a();\nb();\nc();')
    const lines = select(ast, 'pre > code > *')
    expect(lines.map(l => l.tagName + ':' + classes(l).join(' '))).toEqual([
      'div:ec-line glosharp-highlight glosharp-focus-dim',
      'div:ec-line glosharp-diff-add',
      'div:ec-line glosharp-diff-remove glosharp-focus-dim',
    ])
  })
})

describe('keyboard accessibility markup', () => {
  it('makes the first hover token in document order the block tab stop and the rest focusable', async () => {
    useStubResults({
      'var a': {
        code: 'var a = 1;\nvar b = a;\n',
        // Deliberately out of document order
        hovers: [hover(1, 8, 1, 'a'), hover(1, 4, 1, 'b'), hover(0, 4, 1, 'a')],
      },
    })
    const { render } = createEngine()
    const { ast } = await render('var a = 1;\nvar b = a;')
    const tokens = select(ast, '.glosharp-hover')
    expect(tokens.map(t => [lineCodeText(t), t.properties.tabIndex])).toEqual([['a', 0], ['b', -1], ['a', -1]])
  })
})
