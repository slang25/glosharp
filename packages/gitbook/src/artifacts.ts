import { mkdir, readFile, readdir, rm, writeFile } from 'node:fs/promises'
import path from 'node:path'
import { DEFAULT_FENCE, findFences, type FenceAttributes } from './fence.js'
import { snippetKey } from './hash.js'
import { DEFAULT_THEMES } from './frame.js'
import { ARTIFACT_FILE_PATTERN, isThemeName } from './snippet-key.js'

/** Schema version of `index.json`. */
export const INDEX_VERSION = 1

export interface SnippetOccurrence {
  /** Path of the Markdown file, relative to the scan root, with `/` separators. */
  file: string
  /** 1-based line of the opening fence. */
  line: number
  /** Theme the fence pins with `theme="…"`, if any. */
  theme?: string
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

/**
 * Compiles one snippet and returns its unexpected compile errors as
 * human-readable lines (empty when it compiles as intended). `occurrence` is
 * where the snippet first appears, for `file:line` reporting.
 */
export type DiagnoseSnippet = (input: {
  code: string
  framework?: string
  occurrence: SnippetOccurrence
}) => Promise<string[]>

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
  /**
   * Delete artifacts in `outDir` that no snippet claims. Only files named
   * `<sha256>.html` inside theme directories (this build's themes, pinned
   * themes, and the themes listed in the previous `index.json`) are ever
   * considered; nothing else in `outDir` is touched.
   */
  prune?: boolean
  render: RenderSnippet
  /** Check each snippet for unexpected compile errors (once per snippet, not per theme). */
  diagnose?: DiagnoseSnippet
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
  /** Artifact paths that were deleted (prune, not check). */
  pruned: string[]
  /** Snippets that could not be rendered, with the renderer's message. */
  failures: { key: string; theme: string; message: string }[]
  /** Snippets with unexpected compile errors (only when `diagnose` is given). */
  compileErrors: { key: string; occurrences: SnippetOccurrence[]; errors: string[] }[]
  /** True when `index.json` was not written because something failed. */
  indexSkipped: boolean
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

  for (const theme of themes) {
    if (!isThemeName(theme)) throw new Error(`Invalid theme name '${theme}': use letters, digits and '-'.`)
  }

  // Refuse to adopt a directory that holds something else's index.json: it is
  // either the wrong --out, or pruning would be guessing.
  const indexPath = path.join(options.outDir, 'index.json')
  const existingIndexText = await readIfExists(indexPath)
  const previousIndex = existingIndexText === null ? null : parseIndex(existingIndexText)
  if (existingIndexText !== null && previousIndex === null) {
    throw new Error(
      `${indexPath} exists but is not a Glo# artifact index. Refusing to overwrite it or prune ` +
        `next to it — point --out at an empty or dedicated directory.`,
    )
  }

  const snippets = await collectSnippets({ files: options.files, root, fence, log })

  const result: BuildResult = {
    snippets,
    changed: [],
    unchanged: [],
    orphaned: [],
    pruned: [],
    failures: [],
    compileErrors: [],
    indexSkipped: false,
  }

  const claimed = new Set<string>()
  const jobs = snippets.flatMap((snippet) => {
    const wanted = new Set([...themes, ...pinnedThemes(snippet)])
    return [...wanted].map((theme) => {
      claimed.add(`${theme}/${snippet.key}.html`)
      return { theme, snippet }
    })
  })

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

  if (options.diagnose) {
    const diagnose = options.diagnose
    await mapConcurrent(snippets, options.concurrency ?? 4, async (snippet) => {
      let errors: string[]
      try {
        errors = await diagnose({ code: snippet.code, framework: snippet.framework, occurrence: snippet.occurrences[0] })
      } catch (error) {
        result.failures.push({ key: snippet.key, theme: '(compile check)', message: error instanceof Error ? error.message : String(error) })
        return
      }
      if (errors.length > 0) result.compileErrors.push({ key: snippet.key, occurrences: snippet.occurrences, errors })
    })
    result.compileErrors.sort((a, b) => compareOccurrence(a.occurrences[0], b.occurrences[0]))
  }

  // An index describing artifacts that failed to render would point readers at
  // files that don't exist; leave the previous one (and every artifact) alone.
  if (result.failures.length > 0) {
    result.indexSkipped = true
    log(`not writing index.json or pruning: ${result.failures.length} render(s) failed`)
  } else {
    const index = buildIndex({ fence, themes, snippets })
    const indexJson = `${JSON.stringify(index, null, 2)}\n`
    if (existingIndexText === indexJson) result.unchanged.push('index.json')
    else {
      result.changed.push('index.json')
      if (!options.check) {
        await mkdir(options.outDir, { recursive: true })
        await writeFile(indexPath, indexJson, 'utf8')
      }
    }
  }

  const themeDirs = new Set([...themes, ...snippets.flatMap(pinnedThemes), ...(previousIndex?.themes ?? [])])
  result.orphaned = await findOrphans(options.outDir, themeDirs, claimed)
  if (options.prune && !options.check && result.failures.length === 0) {
    for (const orphan of result.orphaned) {
      await rm(path.join(options.outDir, orphan), { force: true })
      result.pruned.push(orphan)
      log(`pruned ${orphan}`)
    }
  }

  result.changed.sort()
  result.unchanged.sort()
  return result
}

function pinnedThemes(snippet: Snippet): string[] {
  return [...new Set(snippet.occurrences.map((o) => o.theme).filter((t): t is string => !!t))].sort()
}

function compareOccurrence(a: SnippetOccurrence, b: SnippetOccurrence): number {
  return a.file.localeCompare(b.file) || a.line - b.line
}

/** `index.json` contents if they look like ours, else null. */
function parseIndex(text: string): ArtifactIndex | null {
  try {
    const parsed = JSON.parse(text) as Partial<ArtifactIndex>
    if (
      typeof parsed === 'object' &&
      parsed !== null &&
      typeof parsed.version === 'number' &&
      typeof parsed.fence === 'string' &&
      Array.isArray(parsed.themes) &&
      parsed.themes.every((t) => typeof t === 'string' && isThemeName(t)) &&
      typeof parsed.snippets === 'object' &&
      parsed.snippets !== null
    ) {
      return parsed as ArtifactIndex
    }
  } catch {
    // fall through
  }
  return null
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
      const theme = stringAttribute(block.attributes, 'theme')
      if (theme !== undefined && !isThemeName(theme)) {
        throw new Error(`${relative}:${block.line}: invalid theme="${theme}" (use letters, digits and '-').`)
      }
      const occurrence: SnippetOccurrence = { file: relative, line: block.line, ...(theme ? { theme } : {}) }
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
      // Occurrences are recorded by location only; a pinned theme is a
      // rendering detail of the fence, not part of what was published.
      occurrences: [...snippet.occurrences]
        .map(({ file, line }) => ({ file, line }))
        .sort(compareOccurrence),
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
 * Only files that are provably Glo# artifacts are candidates: `<sha256>.html`
 * directly inside a theme directory this build (or the previous index) knows
 * about. A dropped theme's directory surfaces as orphaned rather than being
 * served forever; unrelated files and directories are never reported.
 */
async function findOrphans(outDir: string, themeDirs: Set<string>, claimed: Set<string>): Promise<string[]> {
  const orphans: string[] = []
  for (const dir of themeDirs) {
    if (!isThemeName(dir)) continue
    let entries: string[]
    try {
      entries = (await readdir(path.join(outDir, dir), { withFileTypes: true }))
        .filter((entry) => entry.isFile())
        .map((entry) => entry.name)
    } catch {
      continue
    }
    for (const entry of entries) {
      if (!ARTIFACT_FILE_PATTERN.test(entry)) continue
      const relative = `${dir}/${entry}`
      if (!claimed.has(relative)) orphans.push(relative)
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
