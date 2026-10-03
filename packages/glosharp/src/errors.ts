import type { GloSharpError, GloSharpResult } from './types.js'

export type GloSharpCliErrorKind =
  /** No CLI could be found. */
  | 'not-found'
  /** The CLI could not be started (ENOENT, EACCES, …). */
  | 'spawn'
  /** The CLI exited with a non-zero code. */
  | 'exit'
  /** The CLI ran longer than `timeoutMs` and was killed. */
  | 'timeout'
  /** The caller's `AbortSignal` fired and the CLI was killed. */
  | 'aborted'
  /** The CLI succeeded but its output could not be parsed. */
  | 'invalid-output'

export interface GloSharpCliErrorDetails {
  kind: GloSharpCliErrorKind
  command?: string
  args?: readonly string[]
  exitCode?: number | null
  signal?: string | null
  stderr?: string
  /** The first line(s) of the snippet being processed, for context. */
  snippet?: string
  cause?: unknown
}

/**
 * Thrown by the bridge when the glosharp CLI cannot be found, cannot be
 * started, fails, times out or produces unparseable output. (Compile errors in
 * a snippet are not exceptions — they are reported in the result.)
 */
export class GloSharpCliError extends Error {
  readonly kind: GloSharpCliErrorKind
  readonly command?: string
  readonly args?: readonly string[]
  readonly exitCode?: number | null
  readonly signal?: string | null
  readonly stderr?: string
  readonly snippet?: string

  constructor(message: string, details: GloSharpCliErrorDetails) {
    super(message, details.cause === undefined ? undefined : { cause: details.cause })
    this.name = 'GloSharpCliError'
    this.kind = details.kind
    this.command = details.command
    this.args = details.args
    this.exitCode = details.exitCode
    this.signal = details.signal
    this.stderr = details.stderr
    this.snippet = details.snippet
  }
}

/** True for errors thrown by the bridge itself (as opposed to programming errors). */
export function isGloSharpCliError(error: unknown): error is GloSharpCliError {
  return error instanceof GloSharpCliError
}

/** A short, single-line excerpt of a snippet for error messages. */
export function snippetExcerpt(code: string | undefined, max = 60): string | undefined {
  if (code === undefined) return undefined
  const firstLine = code.split('\n').find((line) => line.trim().length > 0)?.trim() ?? ''
  return firstLine.length > max ? `${firstLine.slice(0, max - 1)}…` : firstLine
}

/**
 * Diagnostics that make a snippet "fail": unexpected errors in the visible
 * code plus errors in hidden (cut) code. Empty when `meta.compileSucceeded` is
 * true.
 */
export function unexpectedErrors(result: GloSharpResult): GloSharpError[] {
  if (result.meta.compileSucceeded) return []
  const all = [...result.errors, ...(result.hiddenErrors ?? [])]
  const failing = all.filter((e) => !e.expected && e.severity === 'error')
  // compileSucceeded=false with nothing unexpected left (shouldn't happen, but
  // don't report "failed with no errors"): fall back to every error.
  return failing.length > 0 ? failing : all.filter((e) => e.severity === 'error')
}
