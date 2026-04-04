import { access, constants } from 'node:fs/promises'
import { join } from 'node:path'

export async function which(name: string): Promise<string | null> {
  const pathDirs = (process.env.PATH ?? '').split(':')
  for (const dir of pathDirs) {
    const fullPath = join(dir, name)
    try {
      await access(fullPath, constants.X_OK)
      return fullPath
    } catch {
      continue
    }
  }
  return null
}
