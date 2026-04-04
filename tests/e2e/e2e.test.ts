import { describe, it, expect, beforeAll } from 'vitest'
import { execFileSync } from 'node:child_process'
import { join } from 'node:path'
import { codeToHtml } from 'shiki'

const CLI_PROJECT = join(__dirname, '../../src/GloSharp.Cli/GloSharp.Cli.csproj')
const SAMPLES_DIR = join(__dirname, '../../samples')
const CLI_TIMEOUT = 30000
const TEST_TIMEOUT = CLI_TIMEOUT + 15000

beforeAll(() => {
  execFileSync('dotnet', ['build', CLI_PROJECT, '-v', 'q'], {
    encoding: 'utf-8',
    timeout: 60000,
  })
}, 120000)

function runCli(file: string): any {
  const stdout = execFileSync('dotnet', ['run', '--no-build', '--project', CLI_PROJECT, '--', 'process', file], {
    encoding: 'utf-8',
    timeout: CLI_TIMEOUT,
  })
  // Extract JSON in case there's any non-JSON output on stdout
  const jsonStart = stdout.indexOf('{')
  const jsonStr = jsonStart > 0 ? stdout.slice(jsonStart) : stdout
  return JSON.parse(jsonStr)
}

describe('End-to-end: CLI → JSON', () => {
  it('local-variables.cs produces correct hovers', { timeout: TEST_TIMEOUT }, () => {
    const result = runCli(join(SAMPLES_DIR, 'local-variables.cs'))

    expect(result.meta.compileSucceeded).toBe(true)
    expect(result.hovers.length).toBeGreaterThanOrEqual(2)
    expect(result.hovers[0].text).toContain('greeting')
    expect(result.hovers[0].text).toContain('string')
    expect(result.hovers[1].text).toContain('count')
    expect(result.hovers[1].text).toContain('int')
  })

  it('method-calls.cs shows overloads', () => {
    const result = runCli(join(SAMPLES_DIR, 'method-calls.cs'))

    expect(result.meta.compileSucceeded).toBe(true)
    const writeLineHover = result.hovers.find((h: any) => h.text.includes('WriteLine'))
    expect(writeLineHover).toBeDefined()
    expect(writeLineHover.text).toContain('overloads')
    expect(writeLineHover.overloadCount).toBeGreaterThan(1)
  })

  it('error-expectation.cs marks expected errors', () => {
    const result = runCli(join(SAMPLES_DIR, 'error-expectation.cs'))

    expect(result.meta.compileSucceeded).toBe(true)
    expect(result.errors.length).toBeGreaterThan(0)
    expect(result.errors[0].code).toBe('CS0103')
    expect(result.errors[0].expected).toBe(true)
  })

  it('cut-marker.cs hides setup code', () => {
    const result = runCli(join(SAMPLES_DIR, 'cut-marker.cs'))

    expect(result.code).not.toContain('StringBuilder()')
    expect(result.code).toContain('sb.Append')
    expect(result.hovers.length).toBeGreaterThan(0)
  })

  it('hide-show.cs hides middle section', () => {
    const result = runCli(join(SAMPLES_DIR, 'hide-show.cs'))

    expect(result.code).not.toContain('helper')
    expect(result.code).toContain('var x = 10')
    expect(result.code).toContain('Console.WriteLine')
  })
})

describe('End-to-end: CLI → JSON → Shiki → HTML', () => {
  it('produces HTML with hover popups', async () => {
    const result = runCli(join(SAMPLES_DIR, 'local-variables.cs'))

    const html = await codeToHtml(result.code, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [{
        name: 'glosharp-inject',
        root(hast: any) {
          // Manually inject the popups to verify the pipeline
          const lines = findLines(hast)
          let anchorIdx = 0
          for (const hover of result.hovers) {
            const line = lines[hover.line]
            if (!line) continue
            const anchor = `--th-${anchorIdx++}`
            injectPopup(line, hover, anchor)
          }
        },
      }],
    })

    expect(html).toContain('glosharp-hover')
    expect(html).toContain('glosharp-popup')
    expect(html).toContain('glosharp-keyword')
    expect(html).toContain('anchor-name')
  })
})

// Minimal HAST helpers for the test
function findLines(node: any): any[] {
  const lines: any[] = []
  function walk(n: any) {
    if (n.tagName === 'span' && n.properties?.class === 'line') lines.push(n)
    if (n.children) for (const c of n.children) walk(c)
  }
  walk(node)
  return lines
}

function injectPopup(line: any, hover: any, anchor: string) {
  if (!line.children) return
  // Find any child span whose text contains the target text
  for (let i = 0; i < line.children.length; i++) {
    const text = getText(line.children[i])
    if (text.includes(hover.targetText)) {
      const partNodes = hover.parts.map((p: any) => ({
        type: 'element', tagName: 'span',
        properties: { class: `glosharp-${p.kind}` },
        children: [{ type: 'text', value: p.text }],
      }))
      line.children.splice(i, 1,
        {
          type: 'element', tagName: 'span',
          properties: { class: 'glosharp-hover', style: `anchor-name: ${anchor}` },
          children: [line.children[i]],
        },
        {
          type: 'element', tagName: 'div',
          properties: { class: 'glosharp-popup', style: `position-anchor: ${anchor}` },
          children: [{ type: 'element', tagName: 'code', properties: { class: 'glosharp-popup-code' }, children: partNodes }],
        },
      )
      return
    }
  }
}

function getText(node: any): string {
  if (node.type === 'text') return node.value ?? ''
  return (node.children ?? []).map(getText).join('')
}
