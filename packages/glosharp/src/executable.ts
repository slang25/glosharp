import { homedir } from 'node:os'
import path from 'node:path'
import { GloSharpCliError } from './errors.js'
import { runCli } from './spawn.js'
import type { GloSharpExecutable } from './types.js'
import { which, type WhichOptions } from './which.js'

/** A resolved way to start the CLI: `command` plus arguments that precede the glosharp ones. */
export interface ResolvedExecutable {
  command: string
  prefix: string[]
  /** How it was found, for diagnostics. */
  source: 'option' | 'env' | 'path' | 'global-tool' | 'local-tool'
}

export interface ResolveExecutableOptions extends WhichOptions {
  /** Directory local tool manifests are resolved from. Defaults to `process.cwd()`. */
  cwd?: string
  /** Defaults to `os.homedir()`. */
  home?: string
}

export const INSTALL_HINT =
  'Install the CLI with one of:\n' +
  '  dotnet tool install --global GloSharp.Cli --prerelease\n' +
  '  dotnet new tool-manifest && dotnet tool install GloSharp.Cli --prerelease   (local tool)\n' +
  'or point at an existing build with the `executable` option or the GLOSHARP_EXECUTABLE ' +
  'environment variable (a path to the glosharp executable or GloSharp.Cli.dll).'

/** Turn an explicit executable spec into a command + argument prefix. */
export function parseExecutable(spec: GloSharpExecutable): { command: string; prefix: string[] } {
  if (typeof spec !== 'string') {
    const [command, ...prefix] = spec
    if (!command) throw new TypeError('executable must not be an empty array')
    return { command, prefix }
  }
  // A framework-dependent build (bin/Release/net8.0/GloSharp.Cli.dll) runs through dotnet.
  if (/\.dll$/i.test(spec)) return { command: 'dotnet', prefix: [spec] }
  return { command: spec, prefix: [] }
}

const memo = new Map<string, Promise<ResolvedExecutable>>()

/**
 * Find the glosharp CLI. Discovery spawns processes (`dotnet tool list`), so the
 * outcome is memoised per process for a given spec, environment and working
 * directory — every snippet after the first reuses it.
 */
export function resolveExecutable(
  spec: GloSharpExecutable | undefined,
  options: ResolveExecutableOptions = {},
): Promise<ResolvedExecutable> {
  const env = options.env ?? process.env
  const cwd = options.cwd ?? process.cwd()
  const key = JSON.stringify([
    spec ?? null,
    env.GLOSHARP_EXECUTABLE ?? null,
    env.PATH ?? env.Path ?? null,
    options.platform ?? process.platform,
    cwd,
    options.home ?? null,
  ])
  let pending = memo.get(key)
  if (!pending) {
    pending = discover(spec, { ...options, env, cwd })
    memo.set(key, pending)
  }
  return pending
}

/** Forget memoised discovery results (e.g. after installing the tool in a long-running process). */
export function clearExecutableCache(): void {
  memo.clear()
}

async function discover(
  spec: GloSharpExecutable | undefined,
  options: ResolveExecutableOptions & { env: NodeJS.ProcessEnv; cwd: string },
): Promise<ResolvedExecutable> {
  if (spec !== undefined) return { ...parseExecutable(spec), source: 'option' }

  const fromEnv = options.env.GLOSHARP_EXECUTABLE?.trim()
  if (fromEnv) return { ...parseExecutable(fromEnv), source: 'env' }

  const onPath = await which('glosharp', options)
  if (onPath) return { command: onPath, prefix: [], source: 'path' }

  // `dotnet tool install -g` puts the shim here, but a fresh shell (or CI step)
  // may not have it on PATH yet.
  const isWindows = (options.platform ?? process.platform) === 'win32'
  const pathApi = isWindows ? path.win32 : path.posix
  const toolsDir = pathApi.join(options.home ?? homedir(), '.dotnet', 'tools')
  const globalTool = await which('glosharp', { ...options, env: { ...options.env, PATH: toolsDir, Path: toolsDir } })
  if (globalTool) return { command: globalTool, prefix: [], source: 'global-tool' }

  const dotnet = await which('dotnet', options)
  if (dotnet && (await hasLocalTool(dotnet))) {
    return { command: dotnet, prefix: ['glosharp'], source: 'local-tool' }
  }

  throw new GloSharpCliError(`glosharp CLI not found.\n${INSTALL_HINT}`, { kind: 'not-found' })
}

async function hasLocalTool(dotnet: string): Promise<boolean> {
  try {
    const { stdout } = await runCli(dotnet, ['tool', 'list', '--local'], { timeoutMs: 60_000 })
    return listsGloSharp(stdout)
  } catch {
    return false
  }
}

/**
 * True when `dotnet tool list` output has a row for the GloSharp.Cli package or
 * a `glosharp` command — not merely the word "glosharp" somewhere (a manifest
 * path can contain it).
 */
export function listsGloSharp(toolListOutput: string): boolean {
  return toolListOutput
    .split(/\r?\n/)
    .slice(2) // header + dashes
    .some((row) => {
      const [packageId, , commands] = row.trim().split(/\s+/)
      if (!packageId) return false
      if (packageId.toLowerCase() === 'glosharp.cli') return true
      return (commands ?? '').split(',').some((c) => c.trim().toLowerCase() === 'glosharp')
    })
}
