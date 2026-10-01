import { spawn, type ChildProcess } from 'node:child_process'
import { GloSharpCliError, snippetExcerpt } from './errors.js'

export interface RunCliOptions {
  /** Written to the child's stdin, then stdin is closed. */
  stdin?: string
  /** Kill the child after this many milliseconds. `0`/undefined: no timeout. */
  timeoutMs?: number
  signal?: AbortSignal
  /** Snippet being processed, for error context. */
  snippet?: string
}

export interface RunCliResult {
  stdout: string
  stderr: string
}

/** Cap on stderr kept for error messages; the CLI can be chatty on restore failures. */
const MAX_STDERR = 64 * 1024

const running = new Set<ChildProcess>()
let exitHookInstalled = false

/**
 * Children are killed when Node exits, so a cancelled docs build does not leave
 * a few dozen Roslyn processes running to completion in the background.
 */
function installExitHook(): void {
  if (exitHookInstalled) return
  exitHookInstalled = true
  process.once('exit', () => {
    for (const child of running) killTree(child)
  })
}

/**
 * Run the glosharp CLI and collect its output.
 *
 * - stdout is collected as Buffers and decoded once, so multi-byte UTF-8
 *   sequences split across chunks survive.
 * - A child that exits before reading all of stdin makes the write fail with
 *   EPIPE; that error is swallowed here and the exit code/stderr are reported
 *   instead (unhandled, it would crash the host process).
 * - Timeouts and aborts kill the child (the whole tree on Windows).
 */
export function runCli(command: string, args: readonly string[], options: RunCliOptions = {}): Promise<RunCliResult> {
  installExitHook()

  return new Promise((resolve, reject) => {
    const context = { command, args, snippet: snippetExcerpt(options.snippet) }
    const where = context.snippet ? ` (snippet: ${context.snippet})` : ''

    if (options.signal?.aborted) {
      reject(new GloSharpCliError(`glosharp run aborted before it started${where}`, { kind: 'aborted', ...context }))
      return
    }

    let child: ChildProcess
    try {
      child = spawn(command, [...args], { stdio: ['pipe', 'pipe', 'pipe'], windowsHide: true })
    } catch (error) {
      reject(spawnError(error, context))
      return
    }
    running.add(child)

    const stdout: Buffer[] = []
    const stderr: Buffer[] = []
    let stderrBytes = 0
    let settled = false
    let timer: NodeJS.Timeout | undefined

    const finish = (error?: GloSharpCliError) => {
      if (settled) return
      settled = true
      running.delete(child)
      if (timer) clearTimeout(timer)
      options.signal?.removeEventListener('abort', onAbort)
      if (error) reject(error)
      else resolve({ stdout: Buffer.concat(stdout).toString('utf8'), stderr: stderrText() })
    }

    const stderrText = () => Buffer.concat(stderr).toString('utf8').trim()

    const onAbort = () => {
      killTree(child)
      finish(new GloSharpCliError(`glosharp run aborted${where}`, { kind: 'aborted', ...context, stderr: stderrText() }))
    }

    child.stdout?.on('data', (chunk: Buffer) => stdout.push(chunk))
    child.stderr?.on('data', (chunk: Buffer) => {
      if (stderrBytes >= MAX_STDERR) return
      stderr.push(chunk)
      stderrBytes += chunk.length
    })

    // EPIPE / ECONNRESET when the child exits without draining stdin. The
    // 'close' handler reports the real failure (exit code + stderr).
    child.stdin?.on('error', () => {})

    child.on('error', (error) => finish(spawnError(error, context)))

    child.on('close', (code, signal) => {
      if (code === 0) {
        finish()
        return
      }
      const err = stderrText()
      const status = code === null ? `was killed by ${signal}` : `exited with code ${code}`
      finish(
        new GloSharpCliError(`glosharp ${status}${where}${err ? `:\n${err}` : ''}`, {
          kind: 'exit',
          ...context,
          exitCode: code,
          signal,
          stderr: err,
        }),
      )
    })

    if (options.timeoutMs && options.timeoutMs > 0) {
      const timeoutMs = options.timeoutMs
      timer = setTimeout(() => {
        killTree(child)
        finish(
          new GloSharpCliError(
            `glosharp timed out after ${formatDuration(timeoutMs)}${where}. ` +
              `A NuGet restore waiting on the network or a credential provider is the usual cause; ` +
              `raise the limit with the timeoutMs option or GLOSHARP_TIMEOUT_MS.`,
            { kind: 'timeout', ...context, stderr: stderrText() },
          ),
        )
      }, timeoutMs)
      timer.unref?.()
    }

    options.signal?.addEventListener('abort', onAbort, { once: true })

    child.stdin?.end(options.stdin ?? '')
  })
}

function spawnError(error: unknown, context: { command: string; args: readonly string[]; snippet?: string }): GloSharpCliError {
  const code = (error as NodeJS.ErrnoException | undefined)?.code
  const message = error instanceof Error ? error.message : String(error)
  const hint =
    code === 'ENOENT'
      ? ` — '${context.command}' does not exist or is not on PATH`
      : code === 'EACCES'
        ? ` — '${context.command}' is not executable`
        : ''
  return new GloSharpCliError(`Failed to start glosharp: ${message}${hint}`, {
    kind: 'spawn',
    ...context,
    cause: error,
  })
}

function killTree(child: ChildProcess): void {
  if (child.exitCode !== null || child.signalCode !== null) return
  if (process.platform === 'win32' && child.pid !== undefined) {
    // `dotnet glosharp` is a muxer plus a child host; kill both.
    try {
      spawn('taskkill', ['/pid', String(child.pid), '/T', '/F'], { stdio: 'ignore', windowsHide: true })
      return
    } catch {
      // fall through to a plain kill
    }
  }
  child.kill('SIGKILL')
}

function formatDuration(ms: number): string {
  return ms % 1000 === 0 ? `${ms / 1000}s` : `${ms}ms`
}
