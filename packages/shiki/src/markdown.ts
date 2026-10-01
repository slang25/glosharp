// Ready-made Markdown integrations: a unified/remark plugin (Docusaurus, MDX,
// Astro's `unified()` processor, plain remark → rehype pipelines) and a Sätteri
// mdast plugin (Astro 7.3+'s default Markdown processor). Both share block
// selection, batching through one bridge instance, and the error policy.
import {
  createGloSharp,
  hasGloSharpMarkers,
  isGloSharpCliError,
  keyOptions,
  snippetKey,
  unexpectedErrors,
  type GloSharpInstance,
  type GloSharpKeyOptions,
  type GloSharpResult,
} from '@glosharp/core'
import path from 'node:path'
import { fileURLToPath } from 'node:url'
import type { Element, ElementContent } from 'hast'
import type { BundledTheme, CodeToHastOptions, ShikiTransformer, ThemeRegistrationAny } from 'shiki'

type ThemeInput = BundledTheme | ThemeRegistrationAny
import { applyGloSharp, type GloSharpRenderOptions } from './render.js'
import { rawMeta, type TransformerGloSharpOptions } from './transformer.js'

export interface GloSharpMarkdownOptions extends Omit<TransformerGloSharpOptions, 'onCliError'> {
  /** Fence languages treated as C#. Default `['csharp', 'cs', 'c#']`. A `glosharp` fence is always an opt-in C# block. */
  languages?: string[]
  /**
   * Only process blocks whose fence meta opts in (`glosharp`), like twoslash's
   * `explicitTrigger`. A RegExp is tested against the meta string instead.
   * Default `false`: blocks with Glo# markers are processed too.
   */
  explicitTrigger?: boolean | RegExp
  /**
   * Unexpected compile errors in a snippet (not declared with `// @errors:`):
   * `'warn'` (default) logs them with the file and line, `'throw'` fails the
   * build, `'ignore'` stays quiet. The errors are rendered either way.
   */
  onCompileError?: 'warn' | 'throw' | 'ignore'
  /**
   * The CLI is missing, crashed or timed out: `'throw'` (default) fails the
   * build with an actionable message; `'warn'` logs once and renders the block
   * as plain code.
   */
  onCliError?: 'throw' | 'warn'
  /**
   * How processed blocks reach the page.
   * - `'transformer'`: leave the block for the site's own Shiki pass, which
   *   must include `transformerGloSharp()` (Astro, `@shikijs/rehype`).
   * - `'html'`: highlight with Shiki here and replace the block with the
   *   result (Docusaurus, plain remark → rehype pipelines).
   * - `'auto'` (default): `'transformer'` if `transformerGloSharp()` has been
   *   created in this process, otherwise `'html'`.
   */
  render?: 'auto' | 'transformer' | 'html'
  /** Shiki settings for `render: 'html'`. Defaults to github-light / github-dark dual themes. */
  shiki?: GloSharpShikiOptions
  /**
   * `render: 'html'` only: how the HTML is put into the tree. `'mdx'` emits a
   * JSX element (Docusaurus/MDX, which would otherwise re-render `<pre>`);
   * `'hast'` emits HAST through `data.hChildren` (remark-rehype). `'auto'`
   * (default) picks `'mdx'` when the processor compiles to JavaScript.
   */
  output?: 'auto' | 'mdx' | 'hast'
  /** Where warnings go. Default `console`. */
  logger?: { warn(message: string): void }
}

export interface GloSharpShikiOptions {
  theme?: ThemeInput
  themes?: Record<string, ThemeInput>
  /** As Shiki's `defaultColor` (dual themes only). */
  defaultColor?: string | false
  /** Extra Shiki transformers, run before the Glo# one. */
  transformers?: ShikiTransformer[]
}

interface CodeNode {
  type: string
  lang?: string | null
  meta?: string | null
  value: string
  position?: { start: { line: number; column?: number } }
  data?: Record<string, unknown>
  [key: string]: unknown
}

interface Selected {
  code: string
  effective: GloSharpKeyOptions
  /** Rewrite a `glosharp` fence language to csharp for highlighting. */
  lang?: string
}

// ---- shared registry between the Markdown pass and `transformerGloSharp()` ----

interface Registry {
  results: Map<string, GloSharpResult>
  byCode: Map<string, string>
  transformerCreated: boolean
}

const REGISTRY_KEY = Symbol.for('glosharp.markdown.registry')
const REGISTRY_LIMIT = 5000

function registry(): Registry {
  const holder = globalThis as unknown as Record<symbol, Registry | undefined>
  return (holder[REGISTRY_KEY] ??= { results: new Map(), byCode: new Map(), transformerCreated: false })
}

function register(key: string, code: string, result: GloSharpResult): void {
  const reg = registry()
  reg.results.delete(key)
  reg.results.set(key, result)
  reg.byCode.set(snippetKey(code), key)
  while (reg.results.size > REGISTRY_LIMIT) reg.results.delete(reg.results.keys().next().value!)
  while (reg.byCode.size > REGISTRY_LIMIT) reg.byCode.delete(reg.byCode.keys().next().value!)
}

const META_KEY = /(?:^|\s)glosharp-key=([0-9a-f]{64})(?=\s|$)/

/**
 * Shiki transformer that applies results computed by `remarkGloSharp` /
 * `satteriGloSharp` (with `render: 'transformer'`). Add it to the Shiki
 * transformers of the pipeline that highlights your Markdown, e.g. Astro's
 * `markdown.shikiConfig.transformers`.
 */
export function transformerGloSharp(options: GloSharpRenderOptions = {}): ShikiTransformer {
  registry().transformerCreated = true
  return {
    name: 'glosharp',
    preprocess(code, shikiOptions) {
      const reg = registry()
      const key = META_KEY.exec(rawMeta(shikiOptions) ?? '')?.[1] ?? reg.byCode.get(snippetKey(code))
      const result = key ? reg.results.get(key) : undefined
      ;(this.meta as { glosharp?: GloSharpResult }).glosharp = result
      return result?.code
    },
    root(hast) {
      const result = (this.meta as { glosharp?: GloSharpResult }).glosharp
      if (!result) return
      applyGloSharp({ root: hast, pre: this.pre, lines: this.lines }, result, options)
    },
  }
}

// ---- block selection & processing ----

const DEFAULT_LANGUAGES = ['csharp', 'cs', 'c#']

function metaTokens(meta: string | null | undefined): string[] {
  return (meta ?? '').split(/\s+/).filter(Boolean)
}

function metaAttribute(meta: string | null | undefined, name: string): string | undefined {
  const match = new RegExp(`(?:^|\\s)${name}=(?:"([^"]*)"|'([^']*)'|(\\S+))`).exec(meta ?? '')
  return match ? (match[1] ?? match[2] ?? match[3]) : undefined
}

/** Decide whether a code block is a Glo# block, and with which options. */
export function selectBlock(
  node: { lang?: string | null; meta?: string | null; value: string },
  options: Pick<GloSharpMarkdownOptions, 'languages' | 'explicitTrigger' | 'processUnmarked' | keyof GloSharpKeyOptions> = {},
): Selected | undefined {
  const lang = (node.lang ?? '').toLowerCase()
  const isGloSharpFence = lang === 'glosharp'
  const languages = (options.languages ?? DEFAULT_LANGUAGES).map((l) => l.toLowerCase())
  if (!isGloSharpFence && !languages.includes(lang)) return undefined

  const tokens = metaTokens(node.meta)
  if (tokens.includes('no-glosharp') || tokens.includes('glosharp=false')) return undefined

  const optedIn = isGloSharpFence || tokens.includes('glosharp')
  const trigger = options.explicitTrigger
  const selected =
    trigger instanceof RegExp
      ? isGloSharpFence || trigger.test(node.meta ?? '')
      : trigger
        ? optedIn
        : optedIn || options.processUnmarked || hasGloSharpMarkers(node.value)
  if (!selected) return undefined

  const effective = {
    ...keyOptions(options),
    ...keyOptions({ region: metaAttribute(node.meta, 'region'), framework: metaAttribute(node.meta, 'framework') }),
  }
  return { code: node.value, effective, lang: isGloSharpFence ? 'csharp' : undefined }
}

interface Location {
  file?: string
  /** 1-based line of the opening fence. */
  line?: number
}

class Session {
  readonly glosharp: GloSharpInstance
  readonly warned = new Set<string>()

  constructor(readonly options: GloSharpMarkdownOptions) {
    this.glosharp = createGloSharp(options)
  }

  warn(message: string): void {
    ;(this.options.logger ?? console).warn(message)
  }

  /** Process one block and apply the error policy. `undefined` means "render as plain code". */
  async process(block: Selected, where: Location): Promise<{ key: string; result: GloSharpResult } | undefined> {
    let result: GloSharpResult
    try {
      result = await this.glosharp.process({ code: block.code, ...block.effective })
    } catch (error) {
      const message = `[glosharp] ${describe(where)}: ${error instanceof Error ? error.message : String(error)}`
      if (this.options.onCliError === 'warn' && isGloSharpCliError(error)) {
        // One line per distinct failure; a missing CLI would otherwise repeat per block.
        const reason = error.kind === 'not-found' ? error.message : message
        if (!this.warned.has(reason)) {
          this.warned.add(reason)
          this.warn(error.kind === 'not-found' ? `[glosharp] ${reason}\nC# blocks render as plain code.` : message)
        }
        return undefined
      }
      throw new Error(message, { cause: error })
    }

    for (const warning of result.meta.warnings ?? []) this.warn(`[glosharp] ${describe(where)}: ${warning}`)

    const errors = unexpectedErrors(result)
    if (errors.length > 0 && this.options.onCompileError !== 'ignore') {
      const message = compileErrorMessage(errors, where)
      if (this.options.onCompileError === 'throw') throw new Error(message)
      this.warn(message)
    }

    return { key: snippetKey(block.code, block.effective), result }
  }
}

const sessions = new Map<string, Session>()

/** One bridge instance (and cache) per distinct configuration, shared across files. */
function sessionFor(options: GloSharpMarkdownOptions): Session {
  const id = JSON.stringify(options, (_key, value) =>
    typeof value === 'function' ? `fn:${String(value)}` : value instanceof RegExp ? String(value) : value,
  )
  let session = sessions.get(id)
  if (!session) {
    session = new Session(options)
    sessions.set(id, session)
  }
  return session
}

function describe(where: Location): string {
  if (!where.file) return where.line ? `line ${where.line}` : 'code block'
  return where.line ? `${where.file}:${where.line}` : where.file
}

function compileErrorMessage(errors: GloSharpResult['errors'], where: Location): string {
  const lines = errors.map((e) => {
    const sourceLine = e.sourceLine
    const at =
      where.line !== undefined && sourceLine !== undefined
        ? `${where.file ?? 'line'}${where.file ? ':' : ' '}${where.line + 1 + sourceLine}:${(e.sourceCharacter ?? 0) + 1}`
        : `line ${e.line + 1}`
    return `  ${at} ${e.code}: ${e.message}`
  })
  return (
    `[glosharp] ${describe(where)}: snippet has unexpected compile errors:\n${lines.join('\n')}\n` +
    `  Declare intended errors with \`// @errors: <code>\`, silence them with \`// @noErrors\`, ` +
    `or opt the block out with \`no-glosharp\` in the fence meta.`
  )
}

function relativeFile(file: string | undefined): string | undefined {
  if (!file) return undefined
  const relative = path.relative(process.cwd(), file)
  return relative && !relative.startsWith('..') ? relative.split(path.sep).join('/') : file
}

function resolveRenderMode(options: GloSharpMarkdownOptions): 'transformer' | 'html' {
  if (options.render === 'transformer' || options.render === 'html') return options.render
  return registry().transformerCreated ? 'transformer' : 'html'
}

function withKey(meta: string | null | undefined, key: string): string {
  const base = (meta ?? '').replace(/(?:^|\s)glosharp-key=\S+/g, '').trim()
  return base ? `${base} glosharp-key=${key}` : `glosharp-key=${key}`
}

// ---- remark (unified) ----

interface UnifiedProcessorLike {
  attachers?: Array<[unknown, ...unknown[]]>
}

interface VFileLike {
  path?: string
  history?: string[]
}

/**
 * remark plugin: compiles Glo# code blocks (batched, with bounded
 * concurrency) and renders them with Shiki, or hands the results to
 * `transformerGloSharp()` in the site's own Shiki pass.
 *
 * ```js
 * // Docusaurus: presets → docs → beforeDefaultRemarkPlugins
 * [[remarkGloSharp, { project: './docs/Docs.csproj' }]]
 * ```
 */
export function remarkGloSharp(this: UnifiedProcessorLike | void, options: GloSharpMarkdownOptions = {}) {
  const session = sessionFor(options)
  // unified processors are callable instances, so `typeof` may be 'function'.
  const processor = this && (typeof this === 'object' || typeof this === 'function') ? this : undefined
  const compilesToJs = (processor?.attachers ?? []).some(([plugin]) =>
    /^(?:rehypeRecma|recma\w*|remarkMdx)$/.test((plugin as { name?: string } | undefined)?.name ?? ''),
  )

  return async (tree: { type: string; children?: unknown[] }, file?: VFileLike) => {
    const filePath = relativeFile(file?.path ?? file?.history?.[file.history.length - 1])
    const found: Array<{ node: CodeNode; block: Selected }> = []
    let sawMdx = false
    walk(tree, (node) => {
      if (node.type.startsWith('mdx')) sawMdx = true
      if (node.type !== 'code') return
      const code = node as CodeNode
      const block = selectBlock(code, options)
      if (block) found.push({ node: code, block })
    })
    if (found.length === 0) return

    const mode = resolveRenderMode(options)
    const output = options.output && options.output !== 'auto' ? options.output : compilesToJs || sawMdx ? 'mdx' : 'hast'

    const processed = await Promise.all(
      found.map(({ node, block }) => session.process(block, { file: filePath, line: node.position?.start.line })),
    )

    const highlighter = mode === 'html' && processed.some(Boolean) ? await getHighlighter(options.shiki) : undefined

    found.forEach(({ node, block }, index) => {
      const outcome = processed[index]
      if (!outcome) return
      if (mode === 'transformer') {
        register(outcome.key, block.code, outcome.result)
        node.meta = withKey(node.meta, outcome.key)
        if (block.lang) node.lang = block.lang
        return
      }
      const hast = renderHast(highlighter!, outcome.result, options)
      if (output === 'mdx') replaceWithMdxHtml(node, highlighter!.hastToHtml(hast))
      else replaceWithHast(node, hast)
    })
  }
}

function walk(node: { type: string; children?: unknown[] }, visit: (node: { type: string }) => void): void {
  visit(node)
  for (const child of node.children ?? []) walk(child as { type: string; children?: unknown[] }, visit)
}

// ---- Sätteri (Astro 7.3+) ----

interface SatteriCodeNode {
  lang?: string | null
  meta?: string | null
  value: string
  position?: { start: { line: number } }
}

interface SatteriContext {
  readonly fileURL: URL | undefined
  setProperty(node: unknown, key: string, value: unknown): void
}

/**
 * Sätteri mdast plugin (Astro 7.3+'s default Markdown processor). Pair it with
 * `transformerGloSharp()` in `markdown.shikiConfig.transformers`:
 *
 * ```js
 * import { satteri } from '@astrojs/markdown-satteri'
 * markdown: {
 *   processor: satteri({ mdastPlugins: [satteriGloSharp()] }),
 *   shikiConfig: { transformers: [transformerGloSharp()] },
 * }
 * ```
 */
export function satteriGloSharp(options: Omit<GloSharpMarkdownOptions, 'render' | 'output' | 'shiki'> = {}) {
  const session = sessionFor({ ...options, render: 'transformer' })
  return {
    name: 'glosharp',
    options: { position: true },
    async code(node: Readonly<SatteriCodeNode>, ctx: SatteriContext) {
      const block = selectBlock(node, options)
      if (!block) return
      const file = ctx.fileURL ? relativeFile(fileURLToPath(ctx.fileURL)) : undefined
      const outcome = await session.process(block, { file, line: node.position?.start.line })
      if (!outcome) return
      register(outcome.key, block.code, outcome.result)
      ctx.setProperty(node, 'meta', withKey(node.meta, outcome.key))
      if (block.lang) ctx.setProperty(node, 'lang', block.lang)
    },
  }
}

// ---- rendering with Shiki (render: 'html') ----

type HastRoot = { type: 'root'; children: ElementContent[] }

interface HighlighterLike {
  codeToHast(code: string, options: Record<string, unknown>): HastRoot
  hastToHtml(root: HastRoot): string
  themeOptions: Record<string, unknown>
}

const highlighters = new Map<string, Promise<HighlighterLike>>()

function getHighlighter(shikiOptions: GloSharpShikiOptions = {}): Promise<HighlighterLike> {
  const themes = shikiOptions.themes ?? (shikiOptions.theme ? undefined : { light: 'github-light', dark: 'github-dark' })
  const id = JSON.stringify([shikiOptions.theme ?? null, themes ?? null, shikiOptions.defaultColor ?? null])
  const existing = highlighters.get(id)
  if (existing) return existing
  const pending = (async (): Promise<HighlighterLike> => {
    const shiki = await import('shiki')
    const list = themes ? Object.values(themes) : [shikiOptions.theme!]
    const highlighter = await shiki.createHighlighter({ themes: list as never[], langs: ['csharp'] })
    return {
      codeToHast: (code, options) => highlighter.codeToHast(code, options as never) as unknown as HastRoot,
      hastToHtml: (root) => shiki.hastToHtml(root as never),
      themeOptions: themes
        ? { themes, ...(shikiOptions.defaultColor !== undefined ? { defaultColor: shikiOptions.defaultColor } : {}) }
        : { theme: shikiOptions.theme },
    }
  })()
  highlighters.set(id, pending)
  return pending
}

function renderHast(highlighter: HighlighterLike, result: GloSharpResult, options: GloSharpMarkdownOptions): HastRoot {
  const glosharp: ShikiTransformer = {
    name: 'glosharp',
    root(hast) {
      applyGloSharp({ root: hast, pre: this.pre, lines: this.lines }, result, options)
    },
  }
  return highlighter.codeToHast(result.code, {
    lang: 'csharp',
    ...highlighter.themeOptions,
    transformers: [...(options.shiki?.transformers ?? []), glosharp],
  })
}

function replaceWithHast(node: CodeNode, root: { children: ElementContent[] }): void {
  const children = root.children.filter((c): c is Element => c.type === 'element')
  for (const key of Object.keys(node)) {
    if (key !== 'position') delete node[key]
  }
  node.type = 'glosharp'
  node.value = ''
  node.data = { hName: 'div', hProperties: { className: ['glosharp-block'] }, hChildren: children }
}

function replaceWithMdxHtml(node: CodeNode, html: string): void {
  for (const key of Object.keys(node)) {
    if (key !== 'position') delete node[key]
  }
  Object.assign(node, {
    type: 'mdxJsxFlowElement',
    name: 'div',
    attributes: [
      { type: 'mdxJsxAttribute', name: 'className', value: 'glosharp-block' },
      {
        type: 'mdxJsxAttribute',
        name: 'dangerouslySetInnerHTML',
        value: {
          type: 'mdxJsxAttributeValueExpression',
          value: `{__html: ${JSON.stringify(html)}}`,
          data: {
            estree: {
              type: 'Program',
              sourceType: 'module',
              body: [
                {
                  type: 'ExpressionStatement',
                  expression: {
                    type: 'ObjectExpression',
                    properties: [
                      {
                        type: 'Property',
                        method: false,
                        shorthand: false,
                        computed: false,
                        kind: 'init',
                        key: { type: 'Identifier', name: '__html' },
                        value: { type: 'Literal', value: html, raw: JSON.stringify(html) },
                      },
                    ],
                  },
                },
              ],
            },
          },
        },
      },
    ],
    children: [],
  })
}
