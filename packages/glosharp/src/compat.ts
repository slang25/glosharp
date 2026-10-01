// CLI/bridge version compatibility. The npm packages and GloSharp.Cli are
// released together at one version (scripts/version.mjs); the bridge reads the
// CLI's JSON, so a CLI from a different release line can silently drop or
// misreport fields. This checks the CLI once and warns, rather than failing, so
// a build still produces output.
import { execFile } from 'node:child_process'
import type { ResolvedExecutable } from './executable.js'
import { EXPECTED_CLI_VERSION } from './version.generated.js'

export { EXPECTED_CLI_VERSION }

export interface CliVersion {
  major: number
  minor: number
  patch: number
  prerelease: string[]
  /** The version without build metadata, e.g. `0.1.0-alpha.2`. */
  text: string
}

const VERSION = /(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z-]+(?:\.[0-9A-Za-z-]+)*))?(?:\+[0-9A-Za-z.-]*)?/

/** Find a semver version in `glosharp --version` output (which may carry `+<commit>` build metadata). */
export function parseCliVersion(output: string): CliVersion | undefined {
  const m = VERSION.exec(output)
  if (!m) return undefined
  const prerelease = m[4] ? m[4].split('.') : []
  return {
    major: Number(m[1]),
    minor: Number(m[2]),
    patch: Number(m[3]),
    prerelease,
    text: `${m[1]}.${m[2]}.${m[3]}${m[4] ? `-${m[4]}` : ''}`,
  }
}

/**
 * The release line a version belongs to: `major.minor`, plus the prerelease id
 * for prereleases (`0.1`, `0.1-alpha`, `1.2-rc`). The bridge expects a CLI from
 * its own line.
 */
export function releaseLine(version: CliVersion): string {
  const line = `${version.major}.${version.minor}`
  return version.prerelease.length > 0 ? `${line}-${version.prerelease[0]}` : line
}

const isDevBuild = (v: CliVersion) => v.major === 0 && v.minor === 0 && v.patch === 0

/**
 * Compare the CLI's reported version with the one this package was released
 * with. Returns `undefined` when they're on the same release line, when either
 * is unknown, or when the CLI is a `0.0.0-*` development/CI build.
 */
export function compareCliVersion(
  cliOutput: string,
  expected: string = EXPECTED_CLI_VERSION,
): { expected: CliVersion; actual: CliVersion } | undefined {
  const want = parseCliVersion(expected)
  const got = parseCliVersion(cliOutput)
  if (!want || !got || isDevBuild(want) || isDevBuild(got)) return undefined
  if (releaseLine(want) === releaseLine(got)) return undefined
  return { expected: want, actual: got }
}

export function mismatchMessage(
  executable: Pick<ResolvedExecutable, 'command' | 'prefix' | 'source'>,
  expected: CliVersion,
  actual: CliVersion,
): string {
  const where = [executable.command, ...executable.prefix].join(' ')
  const want = expected.text
  const fix =
    executable.source === 'local-tool'
      ? `  dotnet tool update GloSharp.Cli --version ${want}`
      : executable.source === 'env'
        ? `  point GLOSHARP_EXECUTABLE at a GloSharp.Cli ${want} build, or\n` +
          `  dotnet tool update --global GloSharp.Cli --version ${want}`
        : `  dotnet tool update --global GloSharp.Cli --version ${want}\n` +
          `  (or, for a tool manifest: dotnet tool update GloSharp.Cli --version ${want})`
  return (
    `@glosharp/core ${want} was released with GloSharp.Cli ${want}, but the glosharp CLI it found ` +
    `(${where}) is ${actual.text}. A CLI from another release line can produce output this ` +
    `package misreads (missing hovers, errors or completions). Install the matching CLI:\n` +
    `${fix}\n` +
    `or move the @glosharp/* packages to ${actual.text}. ` +
    `Set GLOSHARP_SKIP_VERSION_CHECK=1 to silence this warning.`
  )
}

export interface CheckCliVersionOptions {
  env?: NodeJS.ProcessEnv
  /** Defaults to {@link EXPECTED_CLI_VERSION}. */
  expected?: string
  /** Defaults to `process.emitWarning`. */
  warn?: (message: string) => void
  timeoutMs?: number
}

const checks = new Map<string, Promise<void>>()
const warned = new Set<string>()

function skipRequested(env: NodeJS.ProcessEnv): boolean {
  const value = env.GLOSHARP_SKIP_VERSION_CHECK?.trim().toLowerCase()
  return !!value && value !== '0' && value !== 'false'
}

function defaultWarn(message: string): void {
  process.emitWarning(message, { type: 'GloSharpVersionWarning', code: 'GLOSHARP_CLI_VERSION_MISMATCH' })
}

function readVersion(command: string, args: string[], timeoutMs: number): Promise<string | undefined> {
  return new Promise((resolve) => {
    try {
      const child = execFile(
        command,
        args,
        { encoding: 'utf8', timeout: timeoutMs, windowsHide: true, maxBuffer: 64 * 1024 },
        (error, stdout) => resolve(error ? undefined : stdout),
      )
      // Nothing to send; a CLI that reads stdin must not wait for it.
      child.stdin?.end()
      child.on('error', () => resolve(undefined))
    } catch {
      resolve(undefined)
    }
  })
}

/**
 * Run `<cli> --version` once per resolved executable and emit a single warning
 * when it is from a different release line than this package. Never throws or
 * rejects: an unknown version is not an error.
 *
 * Executables passed explicitly through the `executable` option are not
 * checked; that is a deliberate choice of build (and what test stubs use).
 * Discovered CLIs (PATH, global or local tool) and `GLOSHARP_EXECUTABLE` are.
 */
export function checkCliVersion(
  executable: ResolvedExecutable,
  options: CheckCliVersionOptions = {},
): Promise<void> {
  const env = options.env ?? process.env
  if (executable.source === 'option' || skipRequested(env)) return Promise.resolve()

  const expected = options.expected ?? EXPECTED_CLI_VERSION
  const key = JSON.stringify([executable.command, executable.prefix, expected])
  let pending = checks.get(key)
  if (!pending) {
    pending = (async () => {
      const output = await readVersion(
        executable.command,
        [...executable.prefix, '--version'],
        options.timeoutMs ?? 30_000,
      )
      if (output === undefined) return
      const mismatch = compareCliVersion(output, expected)
      if (!mismatch) return
      const message = mismatchMessage(executable, mismatch.expected, mismatch.actual)
      if (warned.has(message)) return
      warned.add(message)
      ;(options.warn ?? defaultWarn)(message)
    })().catch(() => {})
    checks.set(key, pending)
  }
  return pending
}

/** Forget which executables were checked (for tests and long-running hosts that reinstall the CLI). */
export function resetCliVersionCheck(): void {
  checks.clear()
  warned.clear()
}
