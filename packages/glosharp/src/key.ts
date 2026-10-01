import { createHash } from 'node:crypto'
import { canonicalizeSnippet } from './snippet.js'
import type { GloSharpProcessOptions } from './types.js'

/**
 * The options that change what the CLI produces for a given snippet. Two
 * blocks with the same code but a different value for any of these must not
 * share a result.
 */
export const RESULT_AFFECTING_OPTIONS = [
  'framework',
  'project',
  'region',
  'noRestore',
  'configFile',
  'complog',
  'complogProject',
] as const

export type GloSharpKeyOptions = Pick<GloSharpProcessOptions, (typeof RESULT_AFFECTING_OPTIONS)[number]>

/**
 * The subset of `options` that affects output, with unset values dropped, in a
 * fixed key order — so `{ project: undefined }` and `{}` fingerprint the same.
 */
export function keyOptions(options: GloSharpKeyOptions | undefined): GloSharpKeyOptions {
  const picked: Record<string, unknown> = {}
  if (!options) return picked
  for (const name of RESULT_AFFECTING_OPTIONS) {
    const value = options[name]
    if (value === undefined || value === null || value === false || value === '') continue
    picked[name] = value
  }
  return picked as GloSharpKeyOptions
}

/**
 * Stable key for a snippet plus the options it is compiled with: SHA-256 (hex)
 * of the canonical snippet (see `canonicalizeSnippet`), followed by the
 * result-affecting options when any are set.
 *
 * With no options this is exactly `sha256(canonicalizeSnippet(code))`, the
 * key `@glosharp/gitbook` publishes artifacts under. Use the same function on
 * both sides of any lookup (batch processing, result maps, artifact stores).
 */
export function snippetKey(code: string, options?: GloSharpKeyOptions): string {
  const hash = createHash('sha256').update(canonicalizeSnippet(code), 'utf8')
  const picked = keyOptions(options)
  if (Object.keys(picked).length > 0) {
    hash.update('\u0000', 'utf8').update(JSON.stringify(picked), 'utf8')
  }
  return hash.digest('hex')
}
