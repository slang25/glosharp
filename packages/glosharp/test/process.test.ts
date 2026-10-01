// Process-level tests: these spawn real child processes (a Node stub standing
// in for the CLI), so stdin/EPIPE, exit codes, timeouts, concurrency and
// decoding are exercised for real.
import { describe, it, expect, beforeEach, afterEach, afterAll } from 'vitest'
import { mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync, mkdirSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'
import { createGloSharp } from '../src/glosharp.js'
import { GloSharpCliError } from '../src/errors.js'
import { configureGloSharp } from '../src/limiter.js'

const STUB = resolve(import.meta.dirname, 'fixtures/stub-cli.mjs')
const stub = (mode: string) => [process.execPath, STUB, mode]

let logDir: string
const originalConcurrency = configureGloSharp().concurrency

// These tests exercise the one-CLI-process-per-snippet path; serve.test.ts covers workers.
configureGloSharp({ workers: 0 })

beforeEach(() => {
  logDir = mkdtempSync(join(tmpdir(), 'glosharp-stub-'))
  process.env.STUB_LOG_DIR = logDir
})

afterEach(() => {
  delete process.env.STUB_LOG_DIR
  rmSync(logDir, { recursive: true, force: true })
  configureGloSharp({ concurrency: originalConcurrency })
})

afterAll(() => {
  configureGloSharp({ concurrency: originalConcurrency })
})

function runs(): Array<{ args: string[]; stdin: string; concurrent: number }> {
  return readdirSync(logDir)
    .filter((f) => f.endsWith('.json'))
    .map((f) => JSON.parse(readFileSync(join(logDir, f), 'utf8')))
}

function isAlive(pid: number): boolean {
  try {
    process.kill(pid, 0)
    return true
  } catch {
    return false
  }
}

describe('process-level robustness', () => {
  it('reports an early CLI exit instead of crashing on EPIPE (large stdin)', async () => {
    const glosharp = createGloSharp({ executable: stub('exit-early') })
    const big = `// ${'x'.repeat(2_000_000)}\nvar x = 1;`

    const error = await glosharp.process({ code: big }).catch((e: unknown) => e)

    expect(error).toBeInstanceOf(GloSharpCliError)
    const cliError = error as GloSharpCliError
    expect(cliError.kind).toBe('exit')
    expect(cliError.exitCode).toBe(1)
    expect(cliError.stderr).toContain('--region cannot be used with --stdin')
    expect(cliError.message).toContain('glosharp exited with code 1')
  })

  it('surfaces exit code, stderr and a snippet excerpt in errors', async () => {
    const glosharp = createGloSharp({ executable: stub('fail') })
    const error = (await glosharp.process({ code: '\nConsole.WriteLine(1);' }).catch((e) => e)) as GloSharpCliError

    expect(error.kind).toBe('exit')
    expect(error.exitCode).toBe(3)
    expect(error.message).toContain('compilation context could not be loaded')
    expect(error.message).toContain('Console.WriteLine(1);')
    expect(error.snippet).toBe('Console.WriteLine(1);')
  })

  it('kills a CLI that exceeds timeoutMs and says so', async () => {
    const glosharp = createGloSharp({ executable: stub('hang'), timeoutMs: 300 })
    const started = Date.now()

    const error = (await glosharp.process({ code: 'var x = 1;' }).catch((e) => e)) as GloSharpCliError

    expect(error).toBeInstanceOf(GloSharpCliError)
    expect(error.kind).toBe('timeout')
    expect(error.message).toMatch(/timed out after 300ms/)
    expect(Date.now() - started).toBeLessThan(5000)

    const pidFile = readdirSync(logDir).find((f) => f.endsWith('.pid'))!
    const pid = Number(readFileSync(join(logDir, pidFile), 'utf8'))
    await new Promise((r) => setTimeout(r, 100))
    expect(isAlive(pid)).toBe(false)
  })

  it('honours an AbortSignal', async () => {
    const glosharp = createGloSharp({ executable: stub('hang') })
    const controller = new AbortController()
    const pending = glosharp.process({ code: 'var x = 1;', signal: controller.signal })
    setTimeout(() => controller.abort(), 100)

    const error = (await pending.catch((e) => e)) as GloSharpCliError
    expect(error.kind).toBe('aborted')
  })

  it('decodes stdout once, so multi-byte characters split across chunks survive', async () => {
    const glosharp = createGloSharp({ executable: stub('utf8') })
    const result = await glosharp.process({ code: 'var x = 1;' })

    expect(result.code).not.toContain('�')
    expect(result.code).toBe('héllo wörld — ✓ 漢字 🎉 '.repeat(4000))
  })

  it('fills in fields that older CLIs omit', async () => {
    const glosharp = createGloSharp({ executable: stub('legacy') })
    const result = await glosharp.process({ code: 'var x = 1;' })

    expect(result.hiddenErrors).toEqual([])
    expect(result.meta.warnings).toEqual([])
  })
})

describe('concurrency', () => {
  it('limits CLI processes across instances with the global limiter', async () => {
    configureGloSharp({ concurrency: 2 })
    const a = createGloSharp({ executable: stub('slow') })
    const b = createGloSharp({ executable: stub('slow') })

    await Promise.all(
      Array.from({ length: 8 }, (_, i) => (i % 2 ? a : b).process({ code: `var x${i} = ${i};` })),
    )

    const all = runs()
    expect(all).toHaveLength(8)
    expect(Math.max(...all.map((r) => r.concurrent))).toBeLessThanOrEqual(2)
  })

  it('applies a per-instance concurrency cap', async () => {
    const glosharp = createGloSharp({ executable: stub('slow'), concurrency: 1 })
    await Promise.all(Array.from({ length: 4 }, (_, i) => glosharp.process({ code: `var y${i} = 0;` })))

    expect(Math.max(...runs().map((r) => r.concurrent))).toBe(1)
  })

  it('shares one CLI run between concurrent identical requests', async () => {
    const glosharp = createGloSharp({ executable: stub('slow') })
    const results = await Promise.all([1, 2, 3].map(() => glosharp.process({ code: 'var same = 1;' })))

    expect(runs()).toHaveLength(1)
    expect(results[0]).toBe(results[1])
  })
})

describe('cache correctness', () => {
  it('keys on options that change the output', async () => {
    const glosharp = createGloSharp({ executable: stub('ok') })
    const code = 'var x = 1;'

    const a = await glosharp.process({ code })
    const b = await glosharp.process({ code, framework: 'net10.0' })
    const c = await glosharp.process({ code, project: './A.csproj' })
    const d = await glosharp.process({ code, project: './B.csproj' })
    const e = await glosharp.process({ code, region: 'intro' })
    const again = await glosharp.process({ code, project: './A.csproj' })

    expect(new Set([a, b, c, d, e]).size).toBe(5)
    expect(again).toBe(c)
    expect(runs()).toHaveLength(5)
  })

  it('re-runs a file whose contents changed', async () => {
    const dir = join(logDir, 'src')
    mkdirSync(dir)
    const file = join(dir, 'Example.cs')
    writeFileSync(file, 'int x = 1;')
    const glosharp = createGloSharp({ executable: stub('ok') })

    await glosharp.process({ file })
    await glosharp.process({ file })
    expect(runs()).toHaveLength(1)

    writeFileSync(file, 'string x = "a much longer replacement";')
    await glosharp.process({ file })
    expect(runs()).toHaveLength(2)
  })

  it('evicts failures so a retry runs again', async () => {
    const glosharp = createGloSharp({ executable: stub('fail') })
    await expect(glosharp.process({ code: 'x' })).rejects.toThrow()
    await expect(glosharp.process({ code: 'x' })).rejects.toThrow()
    expect(readdirSync(logDir).filter((f) => f.endsWith('.json'))).toHaveLength(2)
  })

  it('passes --region together with --stdin', async () => {
    const glosharp = createGloSharp({ executable: stub('ok') })
    await glosharp.process({ code: '#region demo\nvar x = 1;\n#endregion', region: 'demo' })

    expect(runs()[0].args).toEqual(['process', '--stdin', '--region', 'demo'])
    expect(runs()[0].stdin).toContain('#region demo')
  })
})
