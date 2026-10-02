import { describe, it, expect, afterAll } from 'vitest'
import { mkdtempSync, mkdirSync, rmSync, writeFileSync, chmodSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join } from 'node:path'
import { which } from '../src/which.js'
import { listsGloSharp, parseExecutable, resolveExecutable } from '../src/executable.js'
import { GloSharpCliError } from '../src/errors.js'

const root = mkdtempSync(join(tmpdir(), 'glosharp-which-'))
afterAll(() => rmSync(root, { recursive: true, force: true }))

function touch(file: string, executable = true) {
  mkdirSync(join(file, '..'), { recursive: true })
  writeFileSync(file, '')
  if (executable) chmodSync(file, 0o755)
}

describe('which', () => {
  it('splits a Windows PATH on ";" and tries PATHEXT (.exe) extensions', async () => {
    // Drive letters contain ':', which a ':' split would break apart.
    const files = new Set(['C:\\tools\\glosharp.cmd', 'C:\\Program Files\\dotnet tools\\glosharp.exe'])
    const checked: string[] = []
    const found = await which('glosharp', {
      platform: 'win32',
      env: { Path: 'C:\\tools;"C:\\Program Files\\dotnet tools";D:\\other', PATHEXT: '.COM;.EXE;.BAT;.CMD' },
      isExecutable: async (file) => {
        checked.push(file)
        return files.has(file)
      },
    })

    // .cmd/.bat are never tried (Node cannot spawn them without a shell).
    expect(found).toBe('C:\\Program Files\\dotnet tools\\glosharp.exe')
    expect(checked).toEqual(['C:\\tools\\glosharp.com', 'C:\\tools\\glosharp.exe', 'C:\\Program Files\\dotnet tools\\glosharp.com', 'C:\\Program Files\\dotnet tools\\glosharp.exe'])
  })

  // These simulate a POSIX host on the real filesystem; Windows paths contain ':' and
  // have no execute bit, so they only make sense off Windows (Windows has its own tests).
  it.skipIf(process.platform === 'win32')('splits a POSIX PATH on ":" and requires the execute bit', async () => {
    const noExec = join(root, 'posix', 'noexec')
    const exec = join(root, 'posix', 'exec')
    touch(join(noExec, 'glosharp'), false)
    touch(join(exec, 'glosharp'))

    const found = await which('glosharp', { platform: 'linux', env: { PATH: `${noExec}:${exec}` } })
    expect(found).toBe(join(exec, 'glosharp'))
  })

  it('returns null when nothing matches', async () => {
    expect(await which('glosharp', { platform: 'linux', env: { PATH: join(root, 'nope') } })).toBeNull()
  })
})

describe('executable resolution', () => {
  it('runs a .dll through dotnet', () => {
    expect(parseExecutable('/x/GloSharp.Cli.dll')).toEqual({ command: 'dotnet', prefix: ['/x/GloSharp.Cli.dll'] })
    expect(parseExecutable(['node', 'stub.mjs'])).toEqual({ command: 'node', prefix: ['stub.mjs'] })
    expect(parseExecutable('/usr/bin/glosharp')).toEqual({ command: '/usr/bin/glosharp', prefix: [] })
  })

  it('honours GLOSHARP_EXECUTABLE', async () => {
    const resolved = await resolveExecutable(undefined, {
      env: { GLOSHARP_EXECUTABLE: '/opt/glosharp/GloSharp.Cli.dll', PATH: '' },
      cwd: root,
    })
    expect(resolved).toEqual({ command: 'dotnet', prefix: ['/opt/glosharp/GloSharp.Cli.dll'], source: 'env' })
  })

  it.skipIf(process.platform === 'win32')('finds the global tool in ~/.dotnet/tools when it is not on PATH', async () => {
    const home = join(root, 'home')
    touch(join(home, '.dotnet', 'tools', 'glosharp'))
    const resolved = await resolveExecutable(undefined, { env: { PATH: '' }, cwd: root, home, platform: 'linux' })
    expect(resolved.source).toBe('global-tool')
    expect(resolved.command).toBe(join(home, '.dotnet', 'tools', 'glosharp'))
  })

  it('memoises discovery per process', () => {
    const options = { env: { GLOSHARP_EXECUTABLE: '/a/glosharp', PATH: '' }, cwd: root }
    expect(resolveExecutable(undefined, options)).toBe(resolveExecutable(undefined, options))
  })

  it('throws an actionable not-found error', async () => {
    const error = await resolveExecutable(undefined, {
      env: { PATH: join(root, 'empty') },
      cwd: root,
      home: join(root, 'nohome'),
      platform: 'linux',
    }).catch((e) => e)
    expect(error).toBeInstanceOf(GloSharpCliError)
    expect(error.kind).toBe('not-found')
    expect(error.message).toContain('dotnet tool install --global GloSharp.Cli --prerelease')
    expect(error.message).toContain('GLOSHARP_EXECUTABLE')
  })

  it('detects a local tool from `dotnet tool list` rows, not substrings', () => {
    const header = 'Package Id      Version      Commands      Manifest\n' + '-'.repeat(60) + '\n'
    expect(listsGloSharp(header + 'glosharp.cli    0.1.0        glosharp      /repo/.config/dotnet-tools.json\n')).toBe(true)
    // A manifest path containing "glosharp" is not the tool.
    expect(listsGloSharp(header + 'dotnet-ef       9.0.0        dotnet-ef     /src/glosharp/.config/dotnet-tools.json\n')).toBe(false)
    expect(listsGloSharp('No local tools found\n')).toBe(false)
  })
})
