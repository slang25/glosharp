import {
  createGloSharp,
  GloSharpCliError,
  hasGloSharpMarkers,
  keyOptions,
  snippetKey,
  type GloSharpKeyOptions,
  type GloSharpOptions,
  type GloSharpResult,
} from '@glosharp/core'
import { createHash } from 'node:crypto'
import type { ShikiTransformer, ShikiTransformerContext } from 'shiki'
import { applyGloSharp, type GloSharpRenderOptions } from './render.js'

export interface TransformerGloSharpOptions extends GloSharpOptions, GloSharpRenderOptions {
  project?: string
  /** Extract a `#region` by name from each block. */
  region?: string
  noRestore?: boolean
  /**
   * `processGloSharpBlocks` only: also process blocks that contain no Glo#
   * markers (auto-hovers on every block). Default `false` — unmarked blocks are
   * skipped and render as plain code.
   */
  processUnmarked?: boolean
  /**
   * `processGloSharpBlocks` only: what to do when the CLI is missing or fails
   * for a block. `'throw'` (default) rejects with the block's index and an
   * excerpt; `'warn'` logs and leaves that block out of the map.
   */
  onCliError?: 'throw' | 'warn'
}

export interface GloSharpCodeBlock {
  code: string
  project?: string
  region?: string
  framework?: string
  noRestore?: boolean
  /** Process this block even if it has no Glo# markers. */
  force?: boolean
}

/**
 * Results keyed by `snippetKey(code, options)` (from `@glosharp/core`).
 * `keyOptions` records the shared options the batch was processed with, so
 * `transformerGloSharpFromMap` can compute matching keys without being told.
 */
export type GloSharpResultMap = Map<string, GloSharpResult> & { keyOptions?: GloSharpKeyOptions }

export interface TransformerGloSharpFromMapOptions extends GloSharpRenderOptions {
  /** Options the map was keyed with. Defaults to `resultMap.keyOptions`. */
  keyOptions?: GloSharpKeyOptions
  /**
   * Per-block option overrides, for maps built from `GloSharpCodeBlock`s with
   * their own `project`/`region`/…: return the same overrides for the same block.
   * `meta` is the fence meta string, when the pipeline passes one.
   */
  blockOptions?: (code: string, meta: string | undefined) => GloSharpKeyOptions | undefined
}

interface GloSharpMetaBag {
  glosharp?: GloSharpResult
}

/**
 * Shiki transformer for one pre-computed result. Shiki's hooks are
 * synchronous, so the result must be computed first (`processGloSharpCode`).
 */
export function transformerGloSharpWithResult(result: GloSharpResult, options: GloSharpRenderOptions = {}): ShikiTransformer {
  return {
    name: 'glosharp',

    preprocess() {
      return result.code
    },

    root(hast) {
      applyGloSharp({ root: hast, pre: this.pre, lines: linesOf(this, hast) }, result, options)
    },
  }
}

/**
 * Shiki transformer that looks each code block up in a map produced by
 * `processGloSharpBlocks`. Blocks not in the map render untouched. One
 * instance can serve any number of (also concurrent) `codeToHtml` calls.
 */
export function transformerGloSharpFromMap(
  resultMap: GloSharpResultMap,
  options: TransformerGloSharpFromMapOptions = {},
): ShikiTransformer {
  return {
    name: 'glosharp',

    preprocess(code, shikiOptions) {
      const meta = rawMeta(shikiOptions)
      const shared = options.keyOptions ?? resultMap.keyOptions
      const perBlock = options.blockOptions?.(code, meta)
      const result =
        resultMap.get(snippetKey(code, { ...shared, ...perBlock })) ??
        // Maps keyed by a plain sha256 of the raw code (before 0.1.0-alpha.2).
        resultMap.get(createHash('sha256').update(code).digest('hex'))
      ;(this.meta as GloSharpMetaBag).glosharp = result
      return result?.code
    },

    root(hast) {
      const result = (this.meta as GloSharpMetaBag).glosharp
      if (!result) return
      applyGloSharp({ root: hast, pre: this.pre, lines: linesOf(this, hast) }, result, options)
    },
  }
}

/**
 * Process many code blocks with one bridge instance (shared cache, bounded
 * concurrency) and return a map for `transformerGloSharpFromMap`.
 *
 * Blocks without Glo# markers are skipped unless `processUnmarked` is set or
 * the block has `force: true`.
 */
export async function processGloSharpBlocks(
  blocks: Array<string | GloSharpCodeBlock>,
  options: TransformerGloSharpOptions = {},
): Promise<GloSharpResultMap> {
  const glosharp = createGloSharp(options)
  const shared = keyOptions(options)
  const resultMap: GloSharpResultMap = Object.assign(new Map<string, GloSharpResult>(), { keyOptions: shared })

  const tasks = blocks
    .map((block, index) => {
      const b: GloSharpCodeBlock = typeof block === 'string' ? { code: block } : block
      const effective: GloSharpKeyOptions = {
        ...shared,
        ...keyOptions({ project: b.project, region: b.region, framework: b.framework, noRestore: b.noRestore }),
      }
      return { index, code: b.code, force: b.force, effective }
    })
    .filter((task) => options.processUnmarked || task.force || hasGloSharpMarkers(task.code))

  const settled = await Promise.allSettled(
    tasks.map((task) => glosharp.process({ code: task.code, ...task.effective })),
  )

  const failures: Array<{ index: number; code: string; error: unknown }> = []
  settled.forEach((outcome, i) => {
    const task = tasks[i]
    if (outcome.status === 'fulfilled') resultMap.set(snippetKey(task.code, task.effective), outcome.value)
    else failures.push({ index: task.index, code: task.code, error: outcome.reason })
  })

  for (const failure of failures) {
    const error = blockError(failure)
    if (options.onCliError === 'warn') console.warn(`[glosharp] ${error.message}`)
    else throw error
  }

  return resultMap
}

/** Process one snippet (for `transformerGloSharpWithResult`). */
export async function processGloSharpCode(
  code: string,
  options: TransformerGloSharpOptions = {},
): Promise<GloSharpResult> {
  const glosharp = createGloSharp(options)
  return glosharp.process({ code, project: options.project, region: options.region, noRestore: options.noRestore })
}

function blockError(failure: { index: number; code: string; error: unknown }): Error {
  const first = failure.code.split('\n').find((l) => l.trim())?.trim() ?? ''
  const where = `block ${failure.index} (${first.length > 50 ? `${first.slice(0, 49)}…` : first})`
  const { error } = failure
  if (error instanceof GloSharpCliError) {
    return new GloSharpCliError(`${where}: ${error.message}`, {
      kind: error.kind,
      command: error.command,
      args: error.args,
      exitCode: error.exitCode,
      signal: error.signal,
      stderr: error.stderr,
      snippet: error.snippet,
      cause: error,
    })
  }
  return new Error(`${where}: ${error instanceof Error ? error.message : String(error)}`, { cause: error })
}

/** The fence meta string Shiki was given (`meta.__raw`), if any. */
export function rawMeta(options: { meta?: unknown } | undefined): string | undefined {
  const meta = options?.meta as { __raw?: unknown } | undefined
  return typeof meta?.__raw === 'string' ? meta.__raw : undefined
}

type HastRoot = Parameters<NonNullable<ShikiTransformer['root']>>[0]

/** Shiki's line elements. Falls back to a class search for exotic structures. */
function linesOf(context: ShikiTransformerContext, root: HastRoot) {
  if (context.lines && context.lines.length > 0) return context.lines
  const lines: ShikiTransformerContext['lines'][number][] = []
  const visit = (node: { children?: unknown[] }) => {
    for (const child of (node.children ?? []) as Array<{ type: string; tagName?: string; properties?: Record<string, unknown>; children?: unknown[] }>) {
      if (child.type !== 'element') continue
      const cls = child.properties?.class ?? child.properties?.className
      const list = Array.isArray(cls) ? cls.map(String) : typeof cls === 'string' ? cls.split(/\s+/) : []
      if (child.tagName === 'span' && list.includes('line')) lines.push(child as never)
      else visit(child)
    }
  }
  visit(root)
  return lines
}
