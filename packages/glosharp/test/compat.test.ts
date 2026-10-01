import { afterEach, describe, expect, it } from 'vitest'
import {
  checkCliVersion,
  compareCliVersion,
  EXPECTED_CLI_VERSION,
  parseCliVersion,
  releaseLine,
  resetCliVersionCheck,
} from '../src/compat.js'
import type { ResolvedExecutable } from '../src/executable.js'

/** A fake CLI: node printing `output` for `--version`. */
function fakeCli(output: string, source: ResolvedExecutable['source'] = 'path'): ResolvedExecutable {
  return {
    command: process.execPath,
    prefix: ['-e', `process.stdout.write(${JSON.stringify(output)})`, '--'],
    source,
  }
}

afterEach(() => resetCliVersionCheck())

describe('parseCliVersion', () => {
  it('strips build metadata from the informational version', () => {
    expect(parseCliVersion('0.1.0-alpha.2+bcd537b0c1\n')?.text).toBe('0.1.0-alpha.2')
    expect(parseCliVersion('1.2.3\n')?.text).toBe('1.2.3')
    expect(parseCliVersion('Unknown command: --version')).toBeUndefined()
  })

  it('groups versions into release lines', () => {
    expect(releaseLine(parseCliVersion('0.1.0-alpha.7')!)).toBe('0.1-alpha')
    expect(releaseLine(parseCliVersion('0.1.4')!)).toBe('0.1')
    expect(releaseLine(parseCliVersion('2.3.0-rc.1')!)).toBe('2.3-rc')
  })
})

describe('compareCliVersion', () => {
  it('accepts the same release line', () => {
    expect(compareCliVersion('0.1.0-alpha.9', '0.1.0-alpha.2')).toBeUndefined()
    expect(compareCliVersion('0.1.3', '0.1.0')).toBeUndefined()
  })

  it('flags a different major.minor or prerelease line', () => {
    expect(compareCliVersion('0.2.0-alpha.1', '0.1.0-alpha.2')).toBeDefined()
    expect(compareCliVersion('0.1.0-beta.1', '0.1.0-alpha.2')).toBeDefined()
    expect(compareCliVersion('0.1.0', '0.1.0-alpha.2')).toBeDefined()
    expect(compareCliVersion('1.0.0', '0.1.0')).toBeDefined()
  })

  it('ignores unknown versions and 0.0.0 development/CI builds', () => {
    expect(compareCliVersion('garbage', '0.1.0-alpha.2')).toBeUndefined()
    expect(compareCliVersion('0.0.0-ci.123.1+abc', '0.1.0-alpha.2')).toBeUndefined()
  })

  it('defaults to the version this package was released with', () => {
    expect(parseCliVersion(EXPECTED_CLI_VERSION)?.text).toBe(EXPECTED_CLI_VERSION)
    expect(compareCliVersion(`${EXPECTED_CLI_VERSION}+deadbeef`)).toBeUndefined()
  })
})

describe('checkCliVersion', () => {
  const expected = '0.1.0-alpha.2'

  it('warns once when the discovered CLI is from another release line', async () => {
    const warnings: string[] = []
    const warn = (m: string) => warnings.push(m)
    const cli = fakeCli('0.3.0-alpha.1+abc\n')
    await checkCliVersion(cli, { expected, warn, env: {} })
    await checkCliVersion(cli, { expected, warn, env: {} })
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('0.3.0-alpha.1')
    expect(warnings[0]).toContain('dotnet tool update --global GloSharp.Cli --version 0.1.0-alpha.2')
    expect(warnings[0]).toContain('GLOSHARP_SKIP_VERSION_CHECK')
  })

  it('stays quiet for a matching CLI', async () => {
    const warnings: string[] = []
    await checkCliVersion(fakeCli('0.1.0-alpha.2+abc'), { expected, warn: (m) => warnings.push(m), env: {} })
    expect(warnings).toEqual([])
  })

  it('suggests a local tool update for a tool-manifest CLI', async () => {
    const warnings: string[] = []
    await checkCliVersion(fakeCli('0.2.0', 'local-tool'), { expected, warn: (m) => warnings.push(m), env: {} })
    expect(warnings[0]).toContain('dotnet tool update GloSharp.Cli --version 0.1.0-alpha.2')
  })

  it('skips explicit executables and honours GLOSHARP_SKIP_VERSION_CHECK', async () => {
    const warnings: string[] = []
    const warn = (m: string) => warnings.push(m)
    await checkCliVersion(fakeCli('9.9.9', 'option'), { expected, warn, env: {} })
    await checkCliVersion(fakeCli('9.9.8'), { expected, warn, env: { GLOSHARP_SKIP_VERSION_CHECK: '1' } })
    expect(warnings).toEqual([])
  })

  it('never rejects when the CLI cannot report a version', async () => {
    const warnings: string[] = []
    const missing: ResolvedExecutable = { command: '/nonexistent/glosharp', prefix: [], source: 'path' }
    await expect(checkCliVersion(missing, { expected, warn: (m) => warnings.push(m), env: {} })).resolves.toBeUndefined()
    const failing: ResolvedExecutable = { command: process.execPath, prefix: ['-e', 'process.exit(1)', '--'], source: 'path' }
    await expect(checkCliVersion(failing, { expected, warn: (m) => warnings.push(m), env: {} })).resolves.toBeUndefined()
    expect(warnings).toEqual([])
  })
})
