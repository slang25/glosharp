import { readdir, stat } from 'node:fs/promises'
import path from 'node:path'

export const DEFAULT_EXTENSIONS = ['.md', '.mdx']
export const DEFAULT_EXCLUDES = ['node_modules', '.git', '.gitbook']

/**
 * Expand a mix of file and directory paths into a sorted list of Markdown files.
 *
 * Directories are walked recursively. Deliberately not glob-based: a GitBook
 * Git Sync repo is a directory of Markdown, and a stable sort keeps CI output
 * comparable run to run.
 */
export async function collectMarkdownFiles(
  paths: string[],
  options: { extensions?: string[]; excludes?: string[] } = {},
): Promise<string[]> {
  const extensions = options.extensions ?? DEFAULT_EXTENSIONS
  const excludes = new Set(options.excludes ?? DEFAULT_EXCLUDES)
  const found: string[] = []

  async function walk(target: string): Promise<void> {
    const info = await stat(target)
    if (info.isFile()) {
      found.push(target)
      return
    }
    if (!info.isDirectory()) return

    for (const entry of await readdir(target, { withFileTypes: true })) {
      if (excludes.has(entry.name)) continue
      const child = path.join(target, entry.name)
      if (entry.isDirectory()) await walk(child)
      else if (extensions.some((ext) => entry.name.toLowerCase().endsWith(ext))) found.push(child)
    }
  }

  for (const target of paths) await walk(path.resolve(target))

  return [...new Set(found)].sort()
}
