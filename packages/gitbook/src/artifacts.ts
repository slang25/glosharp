import { mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { DEFAULT_FENCE, findFences, type FenceAttributes } from './fence.js'
import { snippetKey } from './hash.js'
import { DEFAULT_THEMES } from './frame.js'

/** Schema version of `index.json`. */
export const INDEX_VERSION = 1

export interface SnippetOccurrence {
  /** Path of the Markdown file, relative to the scan root, with `/` separators. */
  file: string
  /** 1-based line of the opening fence. */
  line: number
}

export interface Snippet {
  key: string
  code: string
  framework?: string
  occurrences: SnippetOccurrence[]
}

export interface ArtifactIndex {
  version: number
  fence: string
  themes: string[]
  snippets: Record<string, { framework?: string; occurrences: SnippetOccurrence[] }>
}

/** Renders one snippet to a self-contained HTML fragment. */
export type RenderSnippet = (input: {
  code: string
  theme: string
  framework?: string
}) => Promise<string>

export interface BuildOptions {
  /** Markdown files to scan, relative to `root` or absolute. */
  files: string[]
  /** Root the recorded occurrence paths are relative to. Defaults to `process.cwd()`. */
  root?: string
  /** Directory the artifacts are written to. */
  outDir: string
  /** Fence language to claim. Defaults to `glosharp`. */
  fence?: string
  /** Themes to render. Defaults to `github-dark` + `github-light`. */
  themes?: string[]
  /** Concurrent renders. Defaults to 4. */
  concurrency?: number
  /** Report what would change without writing anything. */
  check?: boolean
  /** Reuse an artifact that is already on disk instead of re-rendering it. */
  skipExisting?: boolean
  /** Delete artifacts in `outDir` that no snippet claims. */
  prune?: boolean
  render: RenderSnippet
  log?: (message: string) => void
}

export interface BuildResult {
  snippets: Snippet[]
  /** Artifact paths (relative to `outDir`) whose contents changed or appeared. */
  changed: string[]
  /** Artifact paths that were reused unchanged. */
  unchanged: string[]
  /** Artifact paths that no snippet claims. */
  orphaned: string[]
  /** Snippets that could not be rendered, with the renderer's message. */
  failures: { key: string; theme: string; message: string }[]
}

/**
 * Scan Markdown for Glo# fences and publish one pre-rendered HTML fragment per
 * (theme, snippet) under `<outDir>/<theme>/<sha256>.html`, plus a deterministic
 * `index.json` describing what was published.
 *
 * The webframe resolves the same path from the fence body alone, so the layout
 * is the whole contract between CI and the reader's browser.
 */
export async function buildArtifacts(options: BuildOptions): Promise<BuildResult> {
  const root = options.root ?? process.cwd()
  const fence = options.fence ?? DEFAULT_FENCE
  const themes = options.themes ?? [...DEFAULT_THEMES]
  const log = options.log ?? (() => {})

  const snippets = await collectSnippets({ files: options.files, root, fence, log })

  const result: BuildResult = { snippets, changed: [], unchanged: [], orphaned: [], failures: [] }

  const jobs = themes.flatMap((theme) => snippets.map((snippet) => ({ theme, snippet })))
  await mapConcurrent(jobs, options.concurrency ?? 4, async ({ theme, snippet }) => {
    const relative = `${theme}/${snippet.key}.html`
    const target = path.join(options.outDir, relative)
    const existing = await readIfExists(target)

    if (options.skipExisting && existing !== null) {
      result.unchanged.push(relative)
      return
    }

    let html: string
    try {
      html = await options.render({ code: snippet.code, theme, framework: snippet.framework })
    } catch (error) {
      result.failures.push({
        key: snippet.key,
        theme,
        message: error instanceof Error ? error.message : String(error),
      })
      return
    }

    if (existing === html) {
      result.unchanged.push(relative)
      return
    }

    result.changed.push(relative)
    if (!options.check) {
      await mkdir(path.dirname(target), { recursive: true })
      await writeFile(target, html, 'utf8')
    }
  })

  const index = buildIndex({ fence, themes, snippets })
  const indexJson = `${JSON.stringify(index, null, 2)}\n`
  const existingIndex = await readIfExists(path.join(options.outDir, 'index.json'))
  if (existingIndex === indexJson) result.unchanged.push('index.json')
  else {
    result.changed.push('index.json')
    if (!options.check) {
      await mkdir(options.outDir, { recursive: true })
      await writeFile(path.join(options.outDir, 'index.json'), indexJson, 'utf8')
    }
  }

  result.orphaned = await findOrphans(options.outDir, new Set(themes), new Set(snippets.map((s) => s.key)))
  if (options.prune && !options.check) {
    for (const orphan of result.orphaned) {
      await rm(path.join(options.outDir, orphan), { force: true })
      log(`pruned ${orphan}`)
    }
  }

  result.changed.sort()
  result.unchanged.sort()
  return result
}

/**
 * Collect the distinct snippets across a set of Markdown files.
 *
 * Two fences with the same body share one artifact — that is the point of
 * content addressing — but they must then also agree on how it gets compiled,
 * because the key does not cover the fence attributes.
 */
export async function collectSnippets(options: {
  files: string[]
  root: string
  fence: string
  log?: (message: string) => void
}): Promise<Snippet[]> {
  const log = options.log ?? (() => {})
  const byKey = new Map<string, Snippet>()

  for (const file of options.files) {
    const absolute = path.resolve(options.root, file)
    const relative = path.relative(options.root, absolute).split(path.sep).join('/')
    const markdown = await readFile(absolute, 'utf8')

    for (const block of findFences(markdown, options.fence)) {
      const key = snippetKey(block.code)
      const framework = stringAttribute(block.attributes, 'framework')
      const occurrence = { file: relative, line: block.line }
      const existing = byKey.get(key)

      if (!existing) {
        byKey.set(key, { key, code: block.code, framework, occurrences: [occurrence] })
        continue
      }

      if (existing.framework !== framework) {
        throw new Error(
          `Conflicting framework for identical snippets: ` +
            `${existing.occurrences[0].file}:${existing.occurrences[0].line} says ` +
            `${existing.framework ?? '(default)'} but ${relative}:${block.line} says ` +
            `${framework ?? '(default)'}. The artifact key covers the code only, so ` +
            `identical snippets must compile the same way.`,
        )
      }

      existing.occurrences.push(occurrence)
      log(`reusing ${key.slice(0, 12)} for ${relative}:${block.line}`)
    }
  }

  return [...byKey.values()].sort((a, b) => (a.key < b.key ? -1 : 1))
}

/** Build the deterministic `index.json` payload. */
export function buildIndex(input: {
  fence: string
  themes: string[]
  snippets: Snippet[]
}): ArtifactIndex {
  const snippets: ArtifactIndex['snippets'] = {}

  for (const snippet of [...input.snippets].sort((a, b) => (a.key < b.key ? -1 : 1))) {
    snippets[snippet.key] = {
      ...(snippet.framework ? { framework: snippet.framework } : {}),
      occurrences: [...snippet.occurrences].sort(
        (a, b) => a.file.localeCompare(b.file) || a.line - b.line,
      ),
    }
  }

  return {
    version: INDEX_VERSION,
    fence: input.fence,
    themes: [...input.themes].sort(),
    snippets,
  }
}

function stringAttribute(attributes: FenceAttributes, name: string): string | undefined {
  const value = attributes[name]
  return typeof value === 'string' && value.length > 0 ? value : undefined
}

/**
 * Artifacts under `outDir` that this build does not claim.
 *
 * Every theme directory is scanned, not just the ones being built, so dropping a
 * theme from the build surfaces its whole directory as orphaned rather than
 * leaving it to be served forever.
 */
async function findOrphans(
  outDir: string,
  themes: Set<string>,
  keys: Set<string>,
): Promise<string[]> {
  let dirs: string[]
  try {
    dirs = (await readdir(outDir, { withFileTypes: true }))
      .filter((entry) => entry.isDirectory())
      .map((entry) => entry.name)
  } catch {
    return []
  }

  const orphans: string[] = []
  for (const dir of dirs) {
    for (const entry of await readdir(path.join(outDir, dir))) {
      if (!entry.endsWith('.html')) continue
      const claimed = themes.has(dir) && keys.has(entry.slice(0, -'.html'.length))
      if (!claimed) orphans.push(`${dir}/${entry}`)
    }
  }

  return orphans.sort()
}

async function readIfExists(file: string): Promise<string | null> {
  try {
    return await readFile(file, 'utf8')
  } catch {
    return null
  }
}

async function mapConcurrent<T>(
  items: T[],
  limit: number,
  worker: (item: T) => Promise<void>,
): Promise<void> {
  let next = 0
  const runners = Array.from({ length: Math.max(1, Math.min(limit, items.length)) }, async () => {
    while (next < items.length) {
      await worker(items[next++])
    }
  })
  await Promise.all(runners)
}
