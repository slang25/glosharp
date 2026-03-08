import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createTwohash } from '../src/twohash.js'
import type { TwohashResult } from '../src/types.js'
import { spawn } from 'node:child_process'
import { EventEmitter, Readable, Writable } from 'node:stream'

// Mock child_process.spawn
vi.mock('node:child_process', () => ({
  spawn: vi.fn(),
}))

// Mock which to always find the executable
vi.mock('../src/which.js', () => ({
  which: vi.fn().mockResolvedValue('/usr/local/bin/twohash'),
}))

const mockSpawn = vi.mocked(spawn)

function createMockProcess(stdout: string, stderr: string, exitCode: number) {
  const proc = new EventEmitter() as any
  proc.stdout = new Readable({ read() { this.push(stdout); this.push(null) } })
  proc.stderr = new Readable({ read() { this.push(stderr); this.push(null) } })
  proc.stdin = new Writable({ write(_chunk: any, _enc: any, cb: any) { cb() } })
  proc.stdin.end = vi.fn()
  setTimeout(() => proc.emit('close', exitCode), 10)
  return proc
}

const sampleResult: TwohashResult = {
  code: 'var x = 42;',
  original: 'var x = 42;\n//  ^?',
  lang: 'csharp',
  hovers: [{
    line: 0,
    character: 4,
    length: 1,
    text: '(local variable) int x',
    parts: [
      { kind: 'punctuation', text: '(' },
      { kind: 'text', text: 'local variable' },
      { kind: 'punctuation', text: ')' },
      { kind: 'space', text: ' ' },
      { kind: 'keyword', text: 'int' },
      { kind: 'space', text: ' ' },
      { kind: 'localName', text: 'x' },
    ],
    docs: null,
    symbolKind: 'Local',
    targetText: 'x',
  }],
  errors: [],
  completions: [],
  highlights: [],
  hidden: [],
  meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true },
}

describe('createTwohash', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('spawns CLI and parses JSON output', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const twohash = createTwohash()
    const result = await twohash.process({ code: 'var x = 42;\n//  ^?' })

    expect(result.hovers).toHaveLength(1)
    expect(result.hovers[0].text).toBe('(local variable) int x')
    expect(result.meta.compileSucceeded).toBe(true)
  })

  it('passes --stdin flag when code is provided', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const twohash = createTwohash()
    await twohash.process({ code: 'var x = 42;' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/twohash',
      ['process', '--stdin'],
      expect.any(Object),
    )
  })

  it('passes file path when file is provided', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const twohash = createTwohash()
    await twohash.process({ file: 'src/Example.cs' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/twohash',
      ['process', 'src/Example.cs'],
      expect.any(Object),
    )
  })

  it('passes --framework option', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const twohash = createTwohash({ framework: 'net9.0' })
    await twohash.process({ code: 'var x = 42;' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/twohash',
      ['process', '--stdin', '--framework', 'net9.0'],
      expect.any(Object),
    )
  })

  it('caches results for identical code', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const twohash = createTwohash()
    await twohash.process({ code: 'var x = 42;' })

    // Reset mock to verify second call doesn't spawn
    mockSpawn.mockClear()
    const result2 = await twohash.process({ code: 'var x = 42;' })

    expect(mockSpawn).not.toHaveBeenCalled()
    expect(result2.hovers).toHaveLength(1)
  })

  it('throws on non-zero exit code', async () => {
    mockSpawn.mockReturnValue(createMockProcess('', 'Compilation failed', 1))

    const twohash = createTwohash()
    await expect(twohash.process({ code: 'bad code' }))
      .rejects.toThrow('twohash CLI exited with code 1')
  })

  it('throws on invalid JSON output', async () => {
    mockSpawn.mockReturnValue(createMockProcess('not json', '', 0))

    const twohash = createTwohash()
    await expect(twohash.process({ code: 'var x = 42;' }))
      .rejects.toThrow('invalid JSON')
  })

  it('uses custom executable path', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const twohash = createTwohash({ executable: '/custom/path/twohash' })
    await twohash.process({ code: 'var x = 42;' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/custom/path/twohash',
      expect.any(Array),
      expect.any(Object),
    )
  })
})
