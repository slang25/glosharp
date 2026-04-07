import { spawn } from 'node:child_process'
import { createHash } from 'node:crypto'
import { which } from './which.js'
import type { GloSharpOptions, GloSharpProcessOptions, GloSharpResult } from './types.js'

export function createGloSharp(options: GloSharpOptions = {}) {
  const cache = new Map<string, GloSharpResult>()

  async function findExecutable(): Promise<{ command: string; prefix?: string[] }> {
    if (options.executable) return { command: options.executable }
    const found = await which('glosharp')
    if (found) return { command: found }
    // Fall back to local dotnet tool
    const dotnet = await which('dotnet')
    if (dotnet && await spawnCheck(dotnet, ['tool', 'list'])) {
      return { command: dotnet, prefix: ['glosharp'] }
    }
    throw new Error(
      'glosharp not found. Install it with: dotnet tool install -g GloSharp.Cli\n' +
      'Or as a local tool: dotnet tool install GloSharp.Cli --local --add-source .nupkg/'
    )
  }

  async function process(opts: GloSharpProcessOptions): Promise<GloSharpResult> {
    const cacheKey = opts.code
      ? createHash('sha256').update(opts.code).digest('hex')
      : opts.file ?? ''

    const cached = cache.get(cacheKey)
    if (cached) return cached

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

    const configFile = opts.configFile ?? options.configFile
    if (configFile) {
      args.push('--config', configFile)
    }

    const complog = opts.complog ?? options.complog
    if (complog) {
      args.push('--complog', complog)
    }

    const complogProject = opts.complogProject ?? options.complogProject
    if (complogProject) {
      args.push('--complog-project', complogProject)
    }

    const { command, prefix } = await findExecutable()
    const fullArgs = [...(prefix ?? []), ...args]
    const result = await spawnCli(command, fullArgs, opts.code)
    cache.set(cacheKey, result)
    return result
  }

  return { process }
}

function spawnCheck(command: string, args: string[]): Promise<boolean> {
  return new Promise((resolve) => {
    const child = spawn(command, args, { stdio: ['ignore', 'pipe', 'ignore'] })
    let stdout = ''
    child.stdout.on('data', (data: Buffer) => { stdout += data.toString() })
    child.on('error', () => resolve(false))
    child.on('close', () => resolve(stdout.includes('glosharp')))
  })
}

function spawnCli(executable: string, args: string[], stdin?: string): Promise<GloSharpResult> {
  return new Promise((resolve, reject) => {
    const child = spawn(executable, args, { stdio: ['pipe', 'pipe', 'pipe'] })

    let stdout = ''
    let stderr = ''

    child.stdout.on('data', (data: Buffer) => { stdout += data.toString() })
    child.stderr.on('data', (data: Buffer) => { stderr += data.toString() })

    child.on('error', (err: Error) => {
      reject(new Error(`Failed to spawn glosharp: ${err.message}`))
    })

    child.on('close', (code: number | null) => {
      if (code !== 0) {
        reject(new Error(`glosharp exited with code ${code}:\n${stderr}`))
        return
      }

      try {
        const result = JSON.parse(stdout) as GloSharpResult
        resolve(result)
      } catch {
        reject(new Error(`glosharp produced invalid JSON:\n${stdout}`))
      }
    })

    if (stdin) {
      child.stdin.write(stdin)
    }
    child.stdin.end()
  })
}
