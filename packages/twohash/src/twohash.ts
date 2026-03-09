import { spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { which } from './which.js'
import type { TwohashOptions, TwohashProcessOptions, TwohashResult } from './types.js'

export function createTwohash(options: TwohashOptions = {}) {
  const cache = new Map<string, TwohashResult>()

  async function findExecutable(): Promise<string> {
    if (options.executable) return options.executable
    const found = await which('twohash')
    if (!found) {
      throw new Error(
        'twohash CLI not found on PATH. Install it with: dotnet tool install -g twohash'
      )
    }
    return found
  }

  async function process(opts: TwohashProcessOptions): Promise<TwohashResult> {
    const cacheKey = opts.code
      ? createHash('sha256').update(opts.code).digest('hex')
      : opts.file ?? ''

    const cached = cache.get(cacheKey)
    if (cached) return cached

    const executable = await findExecutable()
    const args = ['process']

    if (opts.file) {
      args.push(opts.file)
    } else {
      args.push('--stdin')
    }

    const framework = opts.framework ?? options.framework
    if (framework) {
      args.push('--framework', framework)
    }

    if (opts.project) {
      args.push('--project', opts.project)
    }

    if (opts.region) {
      args.push('--region', opts.region)
    }

    if (opts.noRestore) {
      args.push('--no-restore')
    }

    const cacheDir = opts.cacheDir ?? options.cacheDir
    if (cacheDir) {
      args.push('--cache-dir', cacheDir)
    }

    const result = await spawnCli(executable, args, opts.code)
    cache.set(cacheKey, result)
    return result
  }

  return { process }
}

function spawnCli(executable: string, args: string[], stdin?: string): Promise<TwohashResult> {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, { stdio: ['pipe', 'pipe', 'pipe'] })

    let stdout = ''
    let stderr = ''

    child.stdout.on('data', (data: Buffer) => { stdout += data.toString() })
    child.stderr.on('data', (data: Buffer) => { stderr += data.toString() })

    child.on('error', (err) => {
      reject(new Error(`Failed to spawn twohash CLI: ${err.message}`))
    })

    child.on('close', (code) => {
      if (code !== 0) {
        reject(new Error(`twohash CLI exited with code ${code}:\n${stderr}`))
        return
      }

      try {
        const result = JSON.parse(stdout) as TwohashResult
        resolve(result)
      } catch {
        reject(new Error(`twohash CLI produced invalid JSON:\n${stdout}`))
      }
    })

    if (stdin) {
      child.stdin.write(stdin)
    }
    child.stdin.end()
  })
}
