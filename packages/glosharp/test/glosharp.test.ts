import { describe, it, expect, vi, beforeEach } from 'vitest'
import { createGloSharp } from '../src/glosharp.js'
import type { GloSharpResult } from '../src/types.js'
import { spawn } from 'node:child_process'
import { EventEmitter, Readable, Writable } from 'node:stream'

// Mock child_process.spawn
vi.mock('node:child_process', () => ({
  spawn: vi.fn(),
}))

// Mock which to always find the executable
vi.mock('../src/which.js', () => ({
  which: vi.fn().mockResolvedValue('/usr/local/bin/glosharp'),
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

const sampleResult: GloSharpResult = {
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

describe('createGloSharp', () => {
  beforeEach(() => {
    vi.clearAllMocks()
  })

  it('spawns CLI and parses JSON output', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    const result = await glosharp.process({ code: 'var x = 42;\n//  ^?' })

    expect(result.hovers).toHaveLength(1)
    expect(result.hovers[0].text).toBe('(local variable) int x')
    expect(result.meta.compileSucceeded).toBe(true)
  })

  it('passes --stdin flag when code is provided', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ code: 'var x = 42;' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/glosharp',
      ['process', '--stdin'],
      expect.any(Object),
    )
  })

  it('passes file path when file is provided', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ file: 'src/Example.cs' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/glosharp',
      ['process', 'src/Example.cs'],
      expect.any(Object),
    )
  })

  it('passes --framework option', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp({ framework: 'net9.0' })
    await glosharp.process({ code: 'var x = 42;' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/glosharp',
      ['process', '--stdin', '--framework', 'net9.0'],
      expect.any(Object),
    )
  })

  it('caches results for identical code', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ code: 'var x = 42;' })

    // Reset mock to verify second call doesn't spawn
    mockSpawn.mockClear()
    const result2 = await glosharp.process({ code: 'var x = 42;' })

    expect(mockSpawn).not.toHaveBeenCalled()
    expect(result2.hovers).toHaveLength(1)
  })

  it('throws on non-zero exit code', async () => {
    mockSpawn.mockReturnValue(createMockProcess('', 'Compilation failed', 1))

    const glosharp = createGloSharp()
    await expect(glosharp.process({ code: 'bad code' }))
      .rejects.toThrow('glosharp CLI exited with code 1')
  })

  it('throws on invalid JSON output', async () => {
    mockSpawn.mockReturnValue(createMockProcess('not json', '', 0))

    const glosharp = createGloSharp()
    await expect(glosharp.process({ code: 'var x = 42;' }))
      .rejects.toThrow('invalid JSON')
  })

  it('passes --project option', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ code: 'var x = 42;', project: './MyProject.csproj' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/glosharp',
      ['process', '--stdin', '--project', './MyProject.csproj'],
      expect.any(Object),
    )
  })

  it('passes --no-restore flag', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ code: 'var x = 42;', project: './MyProject.csproj', noRestore: true })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/glosharp',
      ['process', '--stdin', '--project', './MyProject.csproj', '--no-restore'],
      expect.any(Object),
    )
  })

  it('does not pass --no-restore when false', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ code: 'var x = 42;', noRestore: false })

    const callArgs = mockSpawn.mock.calls[0][1] as string[]
    expect(callArgs).not.toContain('--no-restore')
    expect(callArgs).not.toContain('--project')
  })

  it('uses custom executable path', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp({ executable: '/custom/path/glosharp' })
    await glosharp.process({ code: 'var x = 42;' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/custom/path/glosharp',
      expect.any(Array),
      expect.any(Object),
    )
  })

  it('passes --region argument when region is provided', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ file: 'src/Example.cs', region: 'getting-started' })

    expect(mockSpawn).toHaveBeenCalledWith(
      '/usr/local/bin/glosharp',
      ['process', 'src/Example.cs', '--region', 'getting-started'],
      expect.any(Object),
    )
  })

  it('does not pass --region when not provided', async () => {
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(sampleResult), '', 0))

    const glosharp = createGloSharp()
    await glosharp.process({ file: 'src/Example.cs' })

    const callArgs = mockSpawn.mock.calls[0][1] as string[]
    expect(callArgs).not.toContain('--region')
  })

  it('parses completion types from JSON output', async () => {
    const resultWithCompletions = {
      ...sampleResult,
      completions: [{
        line: 0,
        character: 8,
        items: [
          { label: 'WriteLine', kind: 'Method', detail: 'void Console.WriteLine(string?)' },
          { label: 'Write', kind: 'Method', detail: 'void Console.Write(string?)' },
        ],
      }],
    }
    mockSpawn.mockReturnValue(createMockProcess(JSON.stringify(resultWithCompletions), '', 0))

    const glosharp = createGloSharp()
    const result = await glosharp.process({ code: 'Console.\n//      ^|' })

    expect(result.completions).toHaveLength(1)
    expect(result.completions[0].line).toBe(0)
    expect(result.completions[0].character).toBe(8)
    expect(result.completions[0].items).toHaveLength(2)
    expect(result.completions[0].items[0].label).toBe('WriteLine')
    expect(result.completions[0].items[0].kind).toBe('Method')
    expect(result.completions[0].items[0].detail).toBe('void Console.WriteLine(string?)')
  })
})
