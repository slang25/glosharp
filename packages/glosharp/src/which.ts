import { access, constants, stat } from 'node:fs/promises'
import path from 'node:path'

export interface WhichOptions {
  /** Defaults to `process.env`. */
  env?: NodeJS.ProcessEnv
  /** Defaults to `process.platform`. */
  platform?: NodeJS.Platform
  /** File check, injectable for tests. Defaults to a stat (+ X_OK on POSIX). */
  isExecutable?: (file: string, isWindows: boolean) => Promise<boolean>
}

/**
 * Find an executable on PATH, the way the platform's shell would.
 *
 * On Windows PATH is `;`-separated (drive letters contain `:`) and a bare name
 * is tried with each PATHEXT extension. Only `.exe`/`.com` are accepted there:
 * Node refuses to spawn `.cmd`/`.bat` without a shell.
 */
export async function which(name: string, options: WhichOptions = {}): Promise<string | null> {
  const env = options.env ?? process.env
  const platform = options.platform ?? process.platform
  const isWindows = platform === 'win32'
  const pathApi = isWindows ? path.win32 : path.posix

  const pathValue = (isWindows ? (env.Path ?? env.PATH ?? env.path) : env.PATH) ?? ''
  const dirs = pathValue
    .split(isWindows ? ';' : ':')
    .map((dir) => dir.trim().replace(/^"(.*)"$/, '$1'))
    .filter((dir) => dir.length > 0)

  const candidates = isWindows ? windowsCandidates(name, env) : [name]
  const check = options.isExecutable ?? isExecutableFile

  for (const dir of dirs) {
    for (const candidate of candidates) {
      const full = pathApi.join(dir, candidate)
      if (await check(full, isWindows)) return full
    }
  }
  return null
}

function windowsCandidates(name: string, env: NodeJS.ProcessEnv): string[] {
  if (/\.(exe|com)$/i.test(name)) return [name]
  const pathext = (env.PATHEXT ?? '.COM;.EXE;.BAT;.CMD')
    .split(';')
    .map((ext) => ext.trim().toLowerCase())
    .filter((ext) => ext === '.exe' || ext === '.com')
  const exts = pathext.length > 0 ? pathext : ['.exe']
  return exts.map((ext) => name + ext)
}

async function isExecutableFile(file: string, isWindows: boolean): Promise<boolean> {
  try {
    const info = await stat(file)
    if (!info.isFile()) return false
    if (isWindows) return true
    await access(file, constants.X_OK)
    return true
  } catch {
    return false
  }
}
