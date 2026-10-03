// A pool of long-lived `glosharp serve` workers. Each worker keeps the .NET
// runtime, Roslyn and the loaded reference assemblies warm, so a snippet costs
// a compilation instead of a process start (~1 s) plus a compilation.
//
// Protocol (see `glosharp serve --help`): JSON lines over stdin/stdout. The
// first line is a handshake `{"type":"ready","protocol":1,...}`; requests are
// `{"id","command","code","options"}`, responses `{"id","ok","result"|"error"}`
// in completion order.
import { spawn, type ChildProcess } from 'node:child_process'
import { availableParallelism } from 'node:os'
import { GloSharpCliError, snippetExcerpt } from './errors.js'
import type { ResolvedExecutable } from './executable.js'
import { exitMessage, killTree, runCli, timeoutMessage } from './spawn.js'

/** The `glosharp serve` protocol version this bridge speaks. */
export const SERVE_PROTOCOL = 1

const DEFAULT_HANDSHAKE_TIMEOUT_MS = 30_000
const MAX_STDERR_TAIL = 16 * 1024

export type ServeCommand = 'process' | 'render'

/** Options sent with a request: the CLI options in camelCase, plus `cwd`. */
export type ServeRequestOptions = Record<string, string | true>

export interface ServeCall {
  code?: string
  options: ServeRequestOptions
  /** Kill budget in milliseconds; `0` disables. */
  timeoutMs: number
  signal?: AbortSignal
  /** The snippet (or file), for error messages. */
  snippet?: string
  /** The equivalent one-shot command line, for error context. */
  command: string
  args: readonly string[]
}

/** Returned by `ServePool.request` when the request must run as a one-shot CLI instead. */
export const FALLBACK: unique symbol = Symbol('glosharp.serve.fallback')

// ---------------------------------------------------------------------------
// Default pool size
// ---------------------------------------------------------------------------

let defaultWorkerCount: number | undefined

/**
 * Default number of `serve` workers per CLI: `$GLOSHARP_WORKERS`, else
 * `min(2, cpus - 1)` (at least 1).
 *
 * Each worker runs several snippets at once on its own threads and shares one
 * reference/compilation cache between them, so more processes mostly cost
 * memory (~300 MB each once warm): on a 12-core machine, 60 snippets took
 * the same time with 1, 2 or 4 workers, and 8 were slower. Two keep a
 * timed-out or crashed worker from stalling everything.
 */
export function defaultWorkers(): number {
  if (defaultWorkerCount !== undefined) return defaultWorkerCount
  const fromEnv = process.env.GLOSHARP_WORKERS
  if (fromEnv !== undefined && fromEnv.trim() !== '') {
    const n = Number(fromEnv)
    if (Number.isInteger(n) && n >= 0) return n
  }
  return Math.max(1, Math.min(2, availableParallelism() - 1))
}

export function setDefaultWorkers(count: number): void {
  defaultWorkerCount = Number.isFinite(count) && count >= 0 ? Math.floor(count) : undefined
}

// ---------------------------------------------------------------------------
// Pools
// ---------------------------------------------------------------------------

const pools = new Map<string, ServePool>()
const allWorkers = new Set<ServeWorker>()
let exitHookInstalled = false

function installExitHook(): void {
  if (exitHookInstalled) return
  exitHookInstalled = true
  // Workers are unref'd while idle, so they never keep Node alive; make sure
  // they don't outlive it either.
  process.once('exit', () => {
    for (const worker of allWorkers) worker.kill()
  })
}

/** The shared pool for an executable and size (created on first use; no process starts until a request). */
export function getPool(executable: ResolvedExecutable, size: number): ServePool {
  const key = JSON.stringify([executable.command, executable.prefix, size])
  let pool = pools.get(key)
  if (!pool) {
    pool = new ServePool(executable, size)
    pools.set(key, pool)
  }
  return pool
}

/**
 * Stop every `glosharp serve` worker (they finish their current requests
 * first, up to `graceMs`). Workers start again on the next request. Not needed
 * at the end of a build — idle workers don't keep Node running and are killed
 * when it exits — but useful in long-running processes and tests.
 */
export async function closeGloSharpWorkers(graceMs = 5_000): Promise<void> {
  const closing = [...pools.values()]
  pools.clear()
  await Promise.all(closing.map((pool) => pool.close(graceMs)))
}

class WorkerStartError extends Error {
  constructor(
    message: string,
    readonly reason: 'unsupported' | 'protocol' | 'invalid' | 'spawn' | 'exit' | 'timeout',
  ) {
    super(message)
  }
}

export class ServePool {
  readonly #executable: ResolvedExecutable
  readonly #size: number
  #workers: ServeWorker[] = []
  /** `undefined` until a worker has started (or failed to). */
  #supported: boolean | undefined
  #probing = false
  #closed = false

  constructor(executable: ResolvedExecutable, size: number) {
    this.#executable = executable
    this.#size = Math.max(1, Math.floor(size))
  }

  /**
   * Run a request on a worker. Resolves with the result, rejects with a
   * `GloSharpCliError`, or resolves with `FALLBACK` when this CLI can't serve
   * (too old, protocol mismatch, failed to start) and the caller should run
   * the one-shot command instead.
   */
  async request(command: ServeCommand, call: ServeCall): Promise<unknown> {
    if (this.#supported === false || this.#closed) return FALLBACK
    let worker: ServeWorker
    try {
      worker = this.#pick()
    } catch {
      // spawn() threw (invalid arguments): the one-shot run reports it.
      this.#supported ??= false
      return FALLBACK
    }
    this.#probe()
    try {
      return await worker.send(command, call)
    } catch (error) {
      if (!(error instanceof WorkerStartError)) throw error
      this.#unsupported(error)
      return FALLBACK
    }
  }

  /**
   * Alongside the first worker, ask `glosharp serve --help` (stdin closed)
   * whether this CLI has `serve`. An older CLI fails both fast ("unknown
   * command"), but a wrapper or test stub that treats every command as
   * `process` would sit waiting for the end of stdin and never send a
   * handshake; the probe catches that without waiting for the handshake
   * timeout.
   */
  #probe(): void {
    if (this.#probing || this.#supported !== undefined) return
    this.#probing = true
    const { command, prefix } = this.#executable
    runCli(command, [...prefix, 'serve', '--help'], { timeoutMs: handshakeTimeoutMs() }).then(
      ({ stdout }) => {
        if (!/glosharp serve/.test(stdout)) {
          this.#unsupported(new WorkerStartError(`'serve --help' printed no usage: ${excerpt(stdout)}`, 'unsupported'))
        }
      },
      (error: GloSharpCliError) => {
        // Exit code 2 is the CLI's "unknown command"; anything else is a failure worth showing.
        this.#unsupported(
          error.kind === 'spawn'
            ? new WorkerStartError(error.message, 'spawn')
            : error.exitCode === 2
              ? new WorkerStartError(UNKNOWN_COMMAND, 'unsupported')
              : new WorkerStartError(error.message, 'exit'),
        )
      },
    )
  }

  /** This CLI can't serve: say so once, and hand starting workers' requests back for one-shot runs. */
  #unsupported(error: WorkerStartError): void {
    if (this.#supported !== undefined) return
    this.#supported = false
    warnFallback(this.#executable, error)
    for (const worker of this.#workers) worker.abandon(error)
  }

  /** The least-loaded live worker; a new one while the pool has room and every worker is busy. */
  #pick(): ServeWorker {
    const live = this.#workers.filter((w) => w.acceptsWork)
    let best: ServeWorker | undefined
    for (const worker of live) if (!best || worker.load < best.load) best = worker
    if (!best || (best.load > 0 && live.length < this.#size)) {
      best = new ServeWorker(this.#executable, {
        onReady: () => {
          this.#supported = true
        },
        onGone: (gone) => {
          this.#workers = this.#workers.filter((w) => w !== gone)
        },
      })
      this.#workers.push(best)
    }
    return best
  }

  async close(graceMs: number): Promise<void> {
    this.#closed = true
    const workers = this.#workers
    this.#workers = []
    await Promise.all(workers.map((w) => w.close(graceMs)))
  }
}

const warned = new Set<string>()
const UNKNOWN_COMMAND = "'serve' is an unknown command; the CLI is older than @glosharp/core"

function warnFallback(executable: ResolvedExecutable, error: WorkerStartError): void {
  // A CLI that can't be started at all fails the one-shot run too, with a
  // better message; don't add noise.
  if (error.reason === 'spawn') return
  const cli = [executable.command, ...executable.prefix].join(' ')
  if (warned.has(cli)) return
  warned.add(cli)
  const why =
    error.reason === 'unsupported'
      ? `This glosharp CLI (${cli}) doesn't support 'glosharp serve' (${error.message}). ` +
        `Update it (dotnet tool update GloSharp.Cli --prerelease) for much faster builds`
      : error.reason === 'protocol'
        ? `${error.message} (${cli}); use matching versions of the GloSharp.Cli tool and @glosharp/core`
        : `'glosharp serve' failed to start (${cli}): ${error.message}`
  console.warn(`[glosharp] ${why}. Running one CLI process per snippet instead (set workers: 0 to silence this).`)
}

// ---------------------------------------------------------------------------
// Workers
// ---------------------------------------------------------------------------

interface Pending {
  id: number
  command: ServeCommand
  line: string
  call: ServeCall
  resolve: (value: unknown) => void
  reject: (error: unknown) => void
  timer?: NodeJS.Timeout
  onAbort?: () => void
}

interface WorkerHooks {
  onReady: (worker: ServeWorker) => void
  onGone: (worker: ServeWorker) => void
}

interface ResponseMessage {
  id?: unknown
  ok?: unknown
  result?: unknown
  error?: { kind?: unknown; message?: unknown; exitCode?: unknown; stderr?: unknown }
}

class ServeWorker {
  readonly #child: ChildProcess
  readonly #hooks: WorkerHooks
  #state: 'starting' | 'ready' | 'retiring' | 'dead' = 'starting'
  readonly #pending = new Map<number, Pending>()
  /** Requests accepted before the handshake, written once it arrives. */
  #queued: Pending[] = []
  #nextId = 1
  #stdout: Buffer[] = []
  #stderrTail = ''
  #handshakeTimer?: NodeJS.Timeout
  #exited?: Promise<void>
  #refed = true
  #closing = false

  constructor(executable: ResolvedExecutable, hooks: WorkerHooks) {
    installExitHook()
    this.#hooks = hooks

    // Throws synchronously only for invalid arguments; the pool falls back.
    const child = spawn(executable.command, [...executable.prefix, 'serve'], {
      stdio: ['pipe', 'pipe', 'pipe'],
      windowsHide: true,
    })
    this.#child = child
    allWorkers.add(this)
    this.#exited = new Promise((resolve) => child.once('close', () => resolve()))

    child.stdout?.on('data', (chunk: Buffer) => this.#onStdout(chunk))
    child.stderr?.on('data', (chunk: Buffer) => {
      this.#stderrTail = (this.#stderrTail + chunk.toString('utf8')).slice(-MAX_STDERR_TAIL)
    })
    child.stdin?.on('error', () => {}) // EPIPE when the worker dies; 'close' reports it
    child.on('error', (error: NodeJS.ErrnoException) => {
      if (this.#state === 'starting') this.#startFailed(new WorkerStartError(error.message, 'spawn'))
    })
    child.on('close', (code, signal) => this.#onClose(code, signal))

    const handshakeTimeout = handshakeTimeoutMs()
    this.#handshakeTimer = setTimeout(() => {
      this.#startFailed(
        new WorkerStartError(`no handshake within ${handshakeTimeout}ms${this.#stderrSuffix()}`, 'timeout'),
      )
    }, handshakeTimeout)
    this.#handshakeTimer.unref?.()
  }

  /** Requests in flight or queued on this worker. */
  get load(): number {
    return this.#pending.size
  }

  get acceptsWork(): boolean {
    return this.#state === 'starting' || this.#state === 'ready'
  }

  send(command: ServeCommand, call: ServeCall): Promise<unknown> {
    const where = call.snippet ? ` (snippet: ${snippetExcerpt(call.snippet)})` : ''
    const context = { command: call.command, args: call.args, snippet: snippetExcerpt(call.snippet) }

    if (call.signal?.aborted) {
      return Promise.reject(
        new GloSharpCliError(`glosharp run aborted before it started${where}`, { kind: 'aborted', ...context }),
      )
    }

    return new Promise((resolve, reject) => {
      const id = this.#nextId++
      const request: Record<string, unknown> = { id, command, options: call.options }
      if (call.code !== undefined) request.code = call.code
      const pending: Pending = { id, command, line: JSON.stringify(request) + '\n', call, resolve, reject }
      this.#pending.set(id, pending)
      this.#updateRef()

      if (call.timeoutMs > 0) {
        pending.timer = setTimeout(() => {
          this.#settle(id, () =>
            reject(new GloSharpCliError(timeoutMessage(call.timeoutMs, where), { kind: 'timeout', ...context })),
          )
          // The request may be stuck (a restore waiting on a credential
          // prompt): stop sending work here and kill the worker once its
          // other requests are done.
          this.retire()
        }, call.timeoutMs)
        pending.timer.unref?.()
      }

      if (call.signal) {
        pending.onAbort = () => {
          this.#settle(id, () =>
            reject(new GloSharpCliError(`glosharp run aborted${where}`, { kind: 'aborted', ...context })),
          )
          this.retire()
        }
        call.signal.addEventListener('abort', pending.onAbort, { once: true })
      }

      if (this.#state === 'starting') this.#queued.push(pending)
      else this.#write(pending)
    })
  }

  /** Give up on a worker that hasn't finished starting (its requests fall back). */
  abandon(error: WorkerStartError): void {
    this.#startFailed(error)
  }

  /** Take no new work; exit once the requests in flight are answered. */
  retire(): void {
    if (this.#state === 'dead') return
    if (this.#state !== 'retiring') {
      this.#state = 'retiring'
      this.#hooks.onGone(this)
    }
    if (this.#pending.size === 0) this.kill()
  }

  kill(): void {
    killTree(this.#child)
  }

  async close(graceMs: number): Promise<void> {
    if (this.#state === 'ready') this.#state = 'retiring'
    // End of input: the server answers what it has, then exits. Keep Node
    // alive until it has (the caller is awaiting this).
    if (this.#state !== 'dead') {
      this.#closing = true
      this.#updateRef()
      this.#child.stdin?.end()
    }
    const timer = setTimeout(() => this.kill(), graceMs)
    timer.unref?.()
    let giveUp: NodeJS.Timeout | undefined
    await Promise.race([
      this.#exited,
      new Promise<void>((resolve) => {
        giveUp = setTimeout(resolve, graceMs + 1_000)
        giveUp.unref?.()
      }),
    ])
    clearTimeout(timer)
    clearTimeout(giveUp)
  }

  #write(pending: Pending): void {
    this.#child.stdin?.write(pending.line)
  }

  /** Remove a request from the books and run `action` (resolve/reject) once. */
  #settle(id: number, action: () => void): void {
    const pending = this.#pending.get(id)
    if (!pending) return
    this.#pending.delete(id)
    this.#queued = this.#queued.filter((p) => p !== pending)
    if (pending.timer) clearTimeout(pending.timer)
    if (pending.onAbort) pending.call.signal?.removeEventListener('abort', pending.onAbort)
    action()
    this.#updateRef()
    if (this.#state === 'retiring' && this.#pending.size === 0) this.kill()
  }

  /**
   * A busy worker keeps Node alive (like a running one-shot CLI does); an idle
   * one doesn't, so a build exits as soon as its work is done.
   */
  #updateRef(): void {
    const want = this.#pending.size > 0 || this.#closing
    if (want === this.#refed) return
    this.#refed = want
    const method = want ? 'ref' : 'unref'
    this.#child[method]?.()
    for (const stream of [this.#child.stdin, this.#child.stdout, this.#child.stderr]) {
      const handle = stream as unknown as { ref?: () => void; unref?: () => void } | null
      handle?.[method]?.()
    }
  }

  #onStdout(chunk: Buffer): void {
    let start = 0
    let newline = chunk.indexOf(10)
    while (newline !== -1) {
      this.#stdout.push(chunk.subarray(start, newline))
      const line = Buffer.concat(this.#stdout).toString('utf8')
      this.#stdout = []
      if (line.trim() !== '') this.#onLine(line)
      start = newline + 1
      newline = chunk.indexOf(10, start)
    }
    if (start < chunk.length) this.#stdout.push(chunk.subarray(start))
  }

  #onLine(line: string): void {
    if (this.#state === 'dead') return
    let message: unknown
    try {
      message = JSON.parse(line)
    } catch {
      message = undefined
    }

    if (this.#state === 'starting') {
      this.#onHandshake(message, line)
      return
    }

    if (!message || typeof message !== 'object' || !('id' in message) || (message as ResponseMessage).id === null) {
      // Something other than a response on stdout: the stream can't be
      // trusted any more. Fail what's in flight and replace the worker.
      this.#fail(
        (pending) =>
          new GloSharpCliError(`glosharp serve wrote an unexpected line: ${excerpt(line)}`, {
            kind: 'invalid-output',
            command: pending.call.command,
            args: pending.call.args,
            snippet: snippetExcerpt(pending.call.snippet),
          }),
      )
      return
    }

    const response = message as ResponseMessage
    const pending = typeof response.id === 'number' ? this.#pending.get(response.id) : undefined
    if (!pending) return // answered after a timeout or abort
    this.#settle(pending.id, () => {
      if (response.ok === true) pending.resolve(response.result)
      else pending.reject(errorFromResponse(response, pending))
    })
  }

  #onHandshake(message: unknown, line: string): void {
    const handshake = message as { type?: unknown; protocol?: unknown } | undefined
    if (!handshake || typeof handshake !== 'object' || handshake.type !== 'ready') {
      this.#startFailed(new WorkerStartError(`unexpected handshake: ${excerpt(line)}`, 'invalid'))
      return
    }
    if (handshake.protocol !== SERVE_PROTOCOL) {
      this.#startFailed(
        new WorkerStartError(
          `The CLI speaks serve protocol ${JSON.stringify(handshake.protocol)}, ` +
            `but this @glosharp/core speaks protocol ${SERVE_PROTOCOL}`,
          'protocol',
        ),
      )
      return
    }

    if (this.#handshakeTimer) clearTimeout(this.#handshakeTimer)
    // A worker retired while starting still answers what it accepted.
    if (this.#state === 'starting') this.#state = 'ready'
    this.#hooks.onReady(this)
    const queued = this.#queued
    this.#queued = []
    for (const pending of queued) this.#write(pending)
    if (this.#state === 'retiring' && this.#pending.size === 0) this.kill()
  }

  /** The worker can't serve: hand its requests back to the pool (to fall back) and get rid of it. */
  #startFailed(error: WorkerStartError): void {
    if (this.#state === 'dead') return
    if (this.#state === 'ready') return // too late to count as a start failure
    this.#markDead()
    this.kill()
    for (const pending of [...this.#pending.values()]) this.#settle(pending.id, () => pending.reject(error))
  }

  #onClose(code: number | null, signal: NodeJS.Signals | null): void {
    if (this.#state === 'starting') {
      // `glosharp serve` on a CLI without it: "unknown command", exit code 2.
      const unsupported = code === 2 || /unknown command '?serve/i.test(this.#stderrTail)
      this.#startFailed(
        unsupported
          ? new WorkerStartError(UNKNOWN_COMMAND, 'unsupported')
          : new WorkerStartError(
              `exited with ${code === null ? `signal ${signal}` : `code ${code}`} before the handshake${this.#stderrSuffix()}`,
              'exit',
            ),
      )
      return
    }
    const status = code === null ? `was killed by ${signal}` : `exited with code ${code}`
    this.#fail((pending) => {
      const where = pending.call.snippet ? ` (snippet: ${snippetExcerpt(pending.call.snippet)})` : ''
      const stderr = this.#stderrTail.trim()
      return new GloSharpCliError(exitMessage(`serve worker ${status}`, where, stderr), {
        kind: 'exit',
        command: pending.call.command,
        args: pending.call.args,
        snippet: snippetExcerpt(pending.call.snippet),
        exitCode: code,
        signal,
        stderr,
      })
    })
  }

  /** Reject everything in flight and make sure the process is gone. */
  #fail(errorFor: (pending: Pending) => GloSharpCliError): void {
    if (this.#state === 'dead') return
    this.#markDead()
    this.kill()
    for (const pending of [...this.#pending.values()]) this.#settle(pending.id, () => pending.reject(errorFor(pending)))
  }

  #markDead(): void {
    const wasListed = this.#state !== 'retiring'
    this.#state = 'dead'
    if (this.#handshakeTimer) clearTimeout(this.#handshakeTimer)
    allWorkers.delete(this)
    if (wasListed) this.#hooks.onGone(this)
  }

  #stderrSuffix(): string {
    const tail = this.#stderrTail.trim()
    return tail ? `: ${excerpt(tail)}` : ''
  }
}

/** A `{"ok":false}` response as the error the one-shot CLI would have produced. */
function errorFromResponse(response: ResponseMessage, pending: Pending): GloSharpCliError {
  const error = response.error ?? {}
  const where = pending.call.snippet ? ` (snippet: ${snippetExcerpt(pending.call.snippet)})` : ''
  const context = {
    command: pending.call.command,
    args: pending.call.args,
    snippet: snippetExcerpt(pending.call.snippet),
  }
  const message = typeof error.message === 'string' ? error.message : 'unknown error'
  const exitCode = typeof error.exitCode === 'number' ? error.exitCode : 1
  if (error.kind === 'protocol') {
    return new GloSharpCliError(`glosharp serve rejected the request${where}: ${message}`, {
      kind: 'exit',
      ...context,
      exitCode,
      stderr: message,
    })
  }
  const stderr = typeof error.stderr === 'string' ? error.stderr.trim() : message
  return new GloSharpCliError(exitMessage(`exited with code ${exitCode}`, where, stderr), {
    kind: 'exit',
    ...context,
    exitCode,
    stderr,
  })
}

function handshakeTimeoutMs(): number {
  const fromEnv = Number(process.env.GLOSHARP_SERVE_HANDSHAKE_TIMEOUT_MS)
  return Number.isFinite(fromEnv) && fromEnv > 0 ? fromEnv : DEFAULT_HANDSHAKE_TIMEOUT_MS
}

function excerpt(text: string, max = 300): string {
  const oneLine = text.trim()
  return oneLine.length > max ? `${oneLine.slice(0, max)}…` : oneLine
}
