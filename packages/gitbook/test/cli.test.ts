import { describe, it, expect } from 'vitest'
import { parseArgs } from '../src/cli.js'

describe('parseArgs', () => {
  it('reads the command and its paths', () => {
    const args = parseArgs(['build', 'docs', 'README.md', '--out', 'dist'])

    expect(args.command).toBe('build')
    expect(args.paths).toEqual(['docs', 'README.md'])
    expect(args.flags.get('out')).toEqual(['dist'])
  })

  it('collects repeated --theme flags in order', () => {
    const args = parseArgs(['build', 'docs', '--theme', 'github-dark', '--theme', 'github-light'])

    expect(args.flags.get('theme')).toEqual(['github-dark', 'github-light'])
  })

  it('reads boolean flags', () => {
    const args = parseArgs(['build', 'docs', '--check', '--prune'])

    expect(args.bools.has('check')).toBe(true)
    expect(args.bools.has('prune')).toBe(true)
    expect(args.bools.has('skip-existing')).toBe(false)
  })

  it('treats -h and --help alike', () => {
    expect(parseArgs(['-h']).bools.has('help')).toBe(true)
    expect(parseArgs(['--help']).bools.has('help')).toBe(true)
  })

  it('rejects an unknown option rather than silently ignoring it', () => {
    expect(() => parseArgs(['build', 'docs', '--nope', 'x'])).toThrow('Unknown option: --nope')
  })

  it('rejects a value flag with no value', () => {
    expect(() => parseArgs(['build', 'docs', '--out'])).toThrow('--out requires a value')
  })

  it('does not swallow the next option as a value', () => {
    expect(() => parseArgs(['build', 'docs', '--out', '--check'])).toThrow(
      '--out requires a value',
    )
  })

  it('reads the dev command and its options', () => {
    const args = parseArgs(['dev', 'docs', '--port', '4200', '--frame-theme', 'github-dark', '--fresh'])

    expect(args.command).toBe('dev')
    expect(args.paths).toEqual(['docs'])
    expect(args.flags.get('port')).toEqual(['4200'])
    expect(args.flags.get('frame-theme')).toEqual(['github-dark'])
    expect(args.bools.has('fresh')).toBe(true)
    expect(args.bools.has('no-build')).toBe(false)
  })
})
