import { availableParallelism } from 'node:os'
import { defaultWorkers, setDefaultWorkers } from './pool.js'

/** A counting semaphore. `limit` can be changed at any time. */
export class Limiter {
  #limit: number
  #active = 0
  #queue: Array<() => void> = []

  constructor(limit: number) {
    this.#limit = normalizeLimit(limit)
  }

  get limit(): number {
    return this.#limit
  }

  set limit(value: number) {
    this.#limit = normalizeLimit(value)
    this.#drain()
  }

  /** Number of tasks currently holding a slot. */
  get active(): number {
    return this.#active
  }

  /** Number of tasks waiting for a slot. */
  get pending(): number {
    return this.#queue.length
  }

  async run<T>(task: () => Promise<T>): Promise<T> {
    await this.#acquire()
    try {
      return await task()
    } finally {
      this.#release()
    }
  }

  #acquire(): Promise<void> {
    if (this.#active < this.#limit) {
      this.#active++
      return Promise.resolve()
    }
    return new Promise((resolve) => this.#queue.push(resolve))
  }

  #release(): void {
    this.#active--
    this.#drain()
  }

  #drain(): void {
    while (this.#active < this.#limit && this.#queue.length > 0) {
      this.#active++
      this.#queue.shift()!()
    }
  }
}

function normalizeLimit(value: number): number {
  return Number.isFinite(value) && value >= 1 ? Math.floor(value) : 1
}

/**
 * Default process-wide limit: one CLI per spare core, at most 8. Each CLI is a
 * full Roslyn compilation (~100 MB), so "all of them at once" runs CI out of
 * memory on large sites.
 */
export function defaultConcurrency(): number {
  const fromEnv = Number(process.env.GLOSHARP_CONCURRENCY)
  if (Number.isInteger(fromEnv) && fromEnv >= 1) return fromEnv
  return Math.max(1, Math.min(availableParallelism() - 1, 8))
}

/** Shared by every `createGloSharp` instance in the process. */
export const globalLimiter = new Limiter(defaultConcurrency())

export interface GloSharpGlobalOptions {
  /**
   * Maximum snippets processed at once across the whole Node process (CLI
   * processes, or requests in flight on `serve` workers). Defaults to
   * `$GLOSHARP_CONCURRENCY` or `max(1, min(cpus - 1, 8))`.
   */
  concurrency?: number
  /**
   * Default number of `glosharp serve` workers per CLI, for instances that
   * don't set `workers`. `0` runs one CLI process per snippet. Defaults to
   * `$GLOSHARP_WORKERS` or `max(1, min(2, cpus - 1))`.
   */
  workers?: number
}

/** Configure process-wide bridge behaviour. Returns the effective settings. */
export function configureGloSharp(options: GloSharpGlobalOptions = {}): Required<GloSharpGlobalOptions> {
  if (options.concurrency !== undefined) globalLimiter.limit = options.concurrency
  if (options.workers !== undefined) setDefaultWorkers(options.workers)
  return { concurrency: globalLimiter.limit, workers: defaultWorkers() }
}
