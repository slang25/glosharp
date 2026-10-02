import { chmodSync, existsSync, mkdtempSync, readFileSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { ExpressiveCodeEngine, type ExpressiveCodePlugin, type ExpressiveCodeTheme } from '@expressive-code/core'
import { toHtml, selectAll, type Element, type Nodes } from '@expressive-code/core/hast'
import { pluginGloSharp, type PluginGloSharpOptions } from '../src/plugin.js'

export const STUB = join(import.meta.dirname!, 'glosharp-stub.mjs')
chmodSync(STUB, 0o755)
/** Run the stub through node: Windows can't spawn a .mjs file directly (EFTYPE). */
export const STUB_EXECUTABLE: [string, string] = [process.execPath, STUB]

const dir = mkdtempSync(join(tmpdir(), 'glosharp-ec-test-'))
let counter = 0

/** Canned stub responses: `{ "<substring of input>": partial GloSharpResult | { exit, stderr } }`. */
export function useStubResults(map: Record<string, unknown>): void {
  const file = join(dir, `map-${++counter}.json`)
  writeFileSync(file, JSON.stringify(map))
  process.env.GLOSHARP_STUB_MAP = file
}

/** Starts recording the stub's argv; returns a function that reads the calls so far. */
export function recordStubArgs(): () => string[][] {
  const file = join(dir, `args-${++counter}.jsonl`)
  process.env.GLOSHARP_STUB_ARGS = file
  return () => existsSync(file)
    ? readFileSync(file, 'utf-8').trim().split('\n').filter(Boolean).map(l => JSON.parse(l) as string[])
    : []
}

export function resetStub(): void {
  delete process.env.GLOSHARP_STUB_MAP
  delete process.env.GLOSHARP_STUB_ARGS
}

export interface TestEngine {
  engine: ExpressiveCodeEngine
  warnings: string[]
  render(code: string, opts?: { language?: string; meta?: string; file?: string; blockIndex?: number }): Promise<{ ast: Element; html: string }>
}

export function createEngine(
  options: PluginGloSharpOptions = {},
  extra: { plugins?: ExpressiveCodePlugin[]; themes?: ExpressiveCodeTheme[] } = {},
): TestEngine {
  const warnings: string[] = []
  const engine = new ExpressiveCodeEngine({
    plugins: [...(extra.plugins ?? []), pluginGloSharp({ executable: STUB_EXECUTABLE, ...options })],
    themes: extra.themes,
    logger: { warn: (m: string) => warnings.push(m), error: (m: string) => warnings.push(m) },
  })
  return {
    engine,
    warnings,
    async render(code, opts = {}) {
      const { renderedGroupAst } = await engine.render({
        code,
        language: opts.language ?? 'csharp',
        meta: opts.meta ?? '',
        parentDocument: {
          sourceFilePath: opts.file ?? join(process.cwd(), 'src/content/docs/post.md'),
          positionInDocument: { groupIndex: opts.blockIndex ?? 0, totalGroups: 3 },
        },
      })
      return { ast: renderedGroupAst, html: toHtml(renderedGroupAst) }
    },
  }
}

/** Every node of the tree with its ancestors, in document order. */
export function walk(node: Nodes, visit: (node: Nodes, ancestors: Nodes[]) => void, ancestors: Nodes[] = []): void {
  visit(node, ancestors)
  if ('children' in node) {
    for (const child of node.children) walk(child as Nodes, visit, [...ancestors, node])
  }
}

export function classes(el: Element): string[] {
  const c = el.properties?.className
  return Array.isArray(c) ? c.map(String) : typeof c === 'string' ? c.split(' ') : []
}

export function select(ast: Nodes, selector: string): Element[] {
  return selectAll(selector, ast)
}

export function textOf(node: Nodes): string {
  if (node.type === 'text') return node.value
  if ('children' in node) return node.children.map(c => textOf(c as Nodes)).join('')
  return ''
}

/** Text of a rendered `.ec-line`'s code, excluding glosharp popups and the gutter. */
export function lineCodeText(line: Element): string {
  const parts: string[] = []
  walk(line, (node, ancestors) => {
    if (node.type !== 'text') return
    const excluded = ancestors.some(a => a.type === 'element' && classes(a).some(c => c === 'glosharp-popup-container' || c === 'gutter'))
    if (!excluded) parts.push(node.value)
  })
  return parts.join('')
}
