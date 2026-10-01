// `glosharp serve` workers: tested against real child processes (a Node stub
// speaking the serve protocol), so pooling, ordering, timeouts, crashes and
// fallback run for real.
import { describe, it, expect, beforeEach, afterEach, vi } from 'vitest'
import { mkdtempSync, readdirSync, readFileSync, rmSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { createGloSharp } from '../src/glosharp.js'
import { GloSharpCliError } from '../src/errors.js'
import { configureGloSharp } from '../src/limiter.js'
import { closeGloSharpWorkers } from '../src/pool.js'
import type { GloSharpResult } from '../src/types.js'

const SERVE_STUB = resolve(import.meta.dirname, 'fixtures/stub-serve.mjs')
const OLD_CLI_STUB = resolve(import.meta.dirname, 'fixtures/stub-cli.mjs')
const serveStub = (mode = 'ok') => [process.execPath, SERVE_STUB, mode]

type StubResult = GloSharpResult & { via: 'serve' | 'cli'; pid?: number; options?: Record<string, unknown>; args?: string[] }

let logDir: string
let warn: ReturnType<typeof vi.spyOn>

beforeEach(() => {
  logDir = mkdtempSync(join(tmpdir(), 'glosharp-serve-'))
  process.env.STUB_LOG_DIR = logDir
  warn = vi.spyOn(console, 'warn').mockImplementation(() => {})
})

afterEach(async () => {
  await closeGloSharpWorkers(500)
  warn.mockRestore()
  delete process.env.STUB_LOG_DIR
  delete process.env.GLOSHARP_SERVE_HANDSHAKE_TIMEOUT_MS
  rmSync(logDir, { recursive: true, force: true })
})

const files = (suffix: string) => readdirSync(logDir).filter((f) => f.endsWith(suffix))
const workerPids = () => files('.worker').map((f) => Number(f.replace('.worker', '')))
const sleep = (ms: number) => new Promise((r) => setTimeout(r, ms))

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

async function waitUntilDead(pid: number, ms = 3000): Promise<boolean> {
  const until = Date.now() + ms
  while (Date.now() < until) {
    if (!isAlive(pid)) return true
    await sleep(25)
  }
  return !isAlive(pid)
}

describe('worker pool', () => {
  it('starts lazily and reuses one worker across calls and instances', async () => {
    const a = createGloSharp({ executable: serveStub(), workers: 1 })
    const b = createGloSharp({ executable: serveStub(), workers: 1 })
    expect(workerPids()).toHaveLength(0)

    const r1 = (await a.process({ code: 'var a = 1;' })) as StubResult
    const r2 = (await a.process({ code: 'var b = 2;' })) as StubResult
    const r3 = (await b.process({ code: 'var c = 3;' })) as StubResult

    expect([r1.via, r2.via, r3.via]).toEqual(['serve', 'serve', 'serve'])
    expect(new Set([r1.pid, r2.pid, r3.pid]).size).toBe(1)
    expect(workerPids()).toEqual([r1.pid])
    expect(files('.cli')).toHaveLength(0)
  })

  it('spreads concurrent requests over at most `workers` processes', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 2 })
    const results = (await Promise.all(
      Array.from({ length: 8 }, (_, i) => glosharp.process({ code: `// @delay:100\nvar x${i} = ${i};` })),
    )) as StubResult[]

    expect(results.every((r) => r.via === 'serve')).toBe(true)
    expect(workerPids()).toHaveLength(2)
    expect(new Set(results.map((r) => r.pid)).size).toBe(2)
  })

  it('matches responses to requests by id when they arrive out of order', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    const codes = ['// @delay:300\nvar slow = 1;', '// @delay:150\nvar medium = 2;', 'var fast = 3;']

    const results = await Promise.all(codes.map((code) => glosharp.process({ code })))

    expect(results.map((r) => r.code)).toEqual(codes)
    expect(workerPids()).toHaveLength(1)
  })

  it('sends the CLI options, the working directory and the input', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1, framework: 'net9.0', configFile: 'docs/glosharp.config.json' })
    const result = (await glosharp.process({
      code: 'var x = 1;',
      region: 'demo',
      noRestore: true,
      project: 'src/App.csproj',
    })) as StubResult

    expect(result.options).toEqual({
      framework: 'net9.0',
      project: 'src/App.csproj',
      region: 'demo',
      noRestore: true,
      config: 'docs/glosharp.config.json',
      cwd: process.cwd(),
    })

    const fileResult = (await glosharp.process({ file: 'snippets/Intro.cs' })) as StubResult
    expect(fileResult.options).toMatchObject({ file: 'snippets/Intro.cs' })
    const request = readFileSync(join(logDir, `${fileResult.pid}.requests`), 'utf8').trim().split('\n').at(-1)!
    expect(JSON.parse(request)).not.toHaveProperty('code')
  })

  it('renders through the worker', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    expect(await glosharp.render({ code: 'var x = 1;', theme: 'github-light' })).toBe('<pre data-via="serve">var x = 1;</pre>')
  })

  it('workers: 0 runs one CLI process per snippet', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 0 })
    const result = (await glosharp.process({ code: 'var x = 1;' })) as StubResult

    expect(result.via).toBe('cli')
    expect(workerPids()).toHaveLength(0)
  })

  it('configureGloSharp({ workers: 0 }) is the default for instances that do not set it', async () => {
    const original = configureGloSharp().workers
    configureGloSharp({ workers: 0 })
    try {
      const result = (await createGloSharp({ executable: serveStub() }).process({ code: 'var x = 1;' })) as StubResult
      expect(result.via).toBe('cli')
    } finally {
      configureGloSharp({ workers: original })
    }
  })

  it('closeGloSharpWorkers stops the workers; the next request starts a new one', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    const first = (await glosharp.process({ code: 'var a = 1;' })) as StubResult

    await closeGloSharpWorkers()
    expect(await waitUntilDead(first.pid!)).toBe(true)

    const second = (await glosharp.process({ code: 'var b = 2;' })) as StubResult
    expect(second.via).toBe('serve')
    expect(second.pid).not.toBe(first.pid)
  })
})

describe('failures', () => {
  it('turns error responses into the same errors as the one-shot CLI', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })

    const error = (await glosharp.process({ code: '// @fail\nConsole.WriteLine(1);' }).catch((e) => e)) as GloSharpCliError

    expect(error).toBeInstanceOf(GloSharpCliError)
    expect(error.kind).toBe('exit')
    expect(error.exitCode).toBe(1)
    expect(error.stderr).toBe('glosharp process: error: compilation context could not be loaded')
    expect(error.message).toBe(
      'glosharp exited with code 1 (snippet: // @fail):\nglosharp process: error: compilation context could not be loaded',
    )

    const usage = (await glosharp.render({ code: '// @usage' }).catch((e) => e)) as GloSharpCliError
    expect(usage.exitCode).toBe(2)

    // The worker is still fine.
    expect(((await glosharp.process({ code: 'var ok = 1;' })) as StubResult).via).toBe('serve')
    expect(workerPids()).toHaveLength(1)
  })

  it('times out a hung request, kills the worker and carries on with a new one', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1, timeoutMs: 300 })
    const started = Date.now()

    const error = (await glosharp.process({ code: '// @hang' }).catch((e) => e)) as GloSharpCliError

    expect(error).toBeInstanceOf(GloSharpCliError)
    expect(error.kind).toBe('timeout')
    expect(error.message).toMatch(/timed out after 300ms/)
    expect(Date.now() - started).toBeLessThan(5000)
    const [hung] = workerPids()
    expect(await waitUntilDead(hung)).toBe(true)

    const next = (await glosharp.process({ code: 'var x = 1;' })) as StubResult
    expect(next.via).toBe('serve')
    expect(next.pid).not.toBe(hung)
  })

  it('lets the other requests on a timed-out worker finish before killing it', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    const hung = glosharp.process({ code: '// @hang', timeoutMs: 200 }).catch((e) => e)
    const slow = glosharp.process({ code: '// @delay:600\nvar slow = 1;' })

    expect(((await hung) as GloSharpCliError).kind).toBe('timeout')
    const result = (await slow) as StubResult
    expect(result.code).toContain('var slow')
    expect(await waitUntilDead(result.pid!)).toBe(true)
  })

  it('fails only the in-flight requests when a worker crashes, then restarts it', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    const inFlight = glosharp.process({ code: '// @delay:2000\nvar a = 1;' }).catch((e) => e)
    const crashing = glosharp.process({ code: '// @delay:100 @crash\nvar b = 2;' }).catch((e) => e)

    for (const error of [(await inFlight) as GloSharpCliError, (await crashing) as GloSharpCliError]) {
      expect(error).toBeInstanceOf(GloSharpCliError)
      expect(error.kind).toBe('exit')
      expect(error.exitCode).toBe(3)
      expect(error.message).toContain('glosharp serve worker exited with code 3')
      expect(error.stderr).toContain('Internal CLR error')
    }

    const after = (await glosharp.process({ code: 'var c = 3;' })) as StubResult
    expect(after.via).toBe('serve')
    expect(workerPids()).toHaveLength(2)
  })

  it('replaces a worker that writes something other than protocol lines', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    const error = (await glosharp.process({ code: '// @noise' }).catch((e) => e)) as GloSharpCliError

    expect(error.kind).toBe('invalid-output')
    expect(error.message).toContain('debug: something printed to stdout')
    expect(((await glosharp.process({ code: 'var x = 1;' })) as StubResult).via).toBe('serve')
  })

  it('honours an AbortSignal', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    const controller = new AbortController()
    const pending = glosharp.process({ code: '// @hang', signal: controller.signal })
    setTimeout(() => controller.abort(), 100)

    const error = (await pending.catch((e) => e)) as GloSharpCliError
    expect(error.kind).toBe('aborted')
  })

  it('does not cache failures', async () => {
    const glosharp = createGloSharp({ executable: serveStub(), workers: 1 })
    await expect(glosharp.process({ code: '// @fail' })).rejects.toThrow()
    await expect(glosharp.process({ code: '// @fail' })).rejects.toThrow()
    const [pid] = workerPids()
    expect(readFileSync(join(logDir, `${pid}.requests`), 'utf8').trim().split('\n')).toHaveLength(2)
  })
})

describe('fallback to one process per snippet', () => {
  it('falls back for a CLI without `serve`, warning once', async () => {
    const a = createGloSharp({ executable: [process.execPath, OLD_CLI_STUB, 'ok'] })
    const b = createGloSharp({ executable: [process.execPath, OLD_CLI_STUB, 'ok'] })

    const results = (await Promise.all([
      a.process({ code: 'var a = 1;' }),
      a.process({ code: 'var b = 2;' }),
      b.process({ code: 'var c = 3;' }),
    ])) as StubResult[]
    await a.process({ code: 'var d = 4;' })

    expect(results.map((r) => r.code)).toEqual(['var a = 1;', 'var b = 2;', 'var c = 3;'])
    expect(results.every((r) => r.args?.[0] === 'process')).toBe(true)
    expect(warn).toHaveBeenCalledTimes(1)
    expect(String(warn.mock.calls[0][0])).toMatch(/doesn't support 'glosharp serve'/)
  })

  it('falls back on a protocol mismatch', async () => {
    const glosharp = createGloSharp({ executable: serveStub('protocol2'), workers: 1 })
    const result = (await glosharp.process({ code: 'var x = 1;' })) as StubResult

    expect(result.via).toBe('cli')
    expect(warn).toHaveBeenCalledTimes(1)
    expect(String(warn.mock.calls[0][0])).toMatch(/protocol 2.*protocol 1/)
  })

  it('falls back when the handshake is not JSON', async () => {
    const result = (await createGloSharp({ executable: serveStub('garbage'), workers: 1 }).process({ code: 'x' })) as StubResult
    expect(result.via).toBe('cli')
    expect(String(warn.mock.calls[0][0])).toContain('Welcome to glosharp!')
  })

  it('falls back when the worker dies before the handshake', async () => {
    const result = (await createGloSharp({ executable: serveStub('die'), workers: 1 }).render({ code: 'x' }))
    expect(result).toBe('<pre data-via="cli">x</pre>')
    expect(String(warn.mock.calls[0][0])).toContain('TypeLoadException')
  })

  it('falls back quickly for a wrapper that runs every command as `process` (no handshake ever comes)', async () => {
    const started = Date.now()
    const result = (await createGloSharp({ executable: serveStub('naive'), workers: 1 }).process({ code: 'x' })) as StubResult

    expect(result.via).toBe('cli')
    expect(result.args?.[0]).toBe('process')
    expect(Date.now() - started).toBeLessThan(5000) // not the 30 s handshake timeout
    expect(String(warn.mock.calls[0][0])).toMatch(/doesn't support 'glosharp serve'/)
  })

  it('falls back when there is no handshake in time', async () => {
    process.env.GLOSHARP_SERVE_HANDSHAKE_TIMEOUT_MS = '300'
    const result = (await createGloSharp({ executable: serveStub('mute'), workers: 1 }).process({ code: 'x' })) as StubResult
    expect(result.via).toBe('cli')
    expect(String(warn.mock.calls[0][0])).toContain('no handshake within 300ms')
  })

  it('does not warn when the CLI cannot be started at all (the one-shot error says why)', async () => {
    const glosharp = createGloSharp({ executable: join(logDir, 'no-such-glosharp'), workers: 1 })
    const error = (await glosharp.process({ code: 'x' }).catch((e) => e)) as GloSharpCliError

    expect(error.kind).toBe('spawn')
    expect(warn).not.toHaveBeenCalled()
  })
})
