import { describe, it, expect } from 'vitest'
import { DEFAULT_THEMES, renderFrameShell } from '../src/frame.js'
import { AUTO_THEME, normalizeArtifactsUrl } from '../src/config.js'

describe('renderFrameShell', () => {
  const shell = renderFrameShell()

  it('is a complete document', () => {
    expect(shell.startsWith('<!DOCTYPE html>')).toBe(true)
    expect(shell).toContain('<meta name="color-scheme" content="light dark">')
    expect(shell).toContain('id="glosharp-content"')
  })

  it('is deterministic', () => {
    expect(renderFrameShell()).toBe(shell)
  })

  it('announces readiness and reports its own height to the parent', () => {
    expect(shell).toContain("'@webframe.ready'")
    expect(shell).toContain("'@webframe.resize'")
  })

  it('opens popups downward so the frame can grow instead of clipping them', () => {
    expect(shell).toContain('position-area: bottom !important')
    expect(shell).toContain('margin-bottom: 0 !important')
  })

  it('keeps the no-anchor-support fallback pointing downward too', () => {
    expect(shell).toContain('@supports not (anchor-name: --x)')
    expect(shell).toContain('top: 100% !important')
  })

  it('resolves the theme from the reader colour scheme when told to', () => {
    expect(shell).toContain("'(prefers-color-scheme: dark)'")
    expect(shell).toContain(JSON.stringify(DEFAULT_THEMES[0]))
    expect(shell).toContain(JSON.stringify(DEFAULT_THEMES[1]))
  })

  it('looks artifacts up as <base>/<theme>/<key>.html', () => {
    expect(shell).toContain("base + '/' + resolveTheme() + '/' + key + '.html'")
  })

  it('escapes the code in its plain-code fallback', () => {
    expect(shell).toContain('function escapeHtml')
    expect(shell).toContain('escapeHtml(code)')
  })

  it('explains both ways a lookup can come up empty', () => {
    expect(shell).toContain('set the artifacts URL')
    expect(shell).toContain('no rendered snippet published')
  })

  it('honours a custom theme pair', () => {
    const custom = renderFrameShell({ themes: { dark: 'night', light: 'day' } })

    expect(custom).toContain('"night"')
    expect(custom).toContain('"day"')
  })

  it('does not inset the snippet — the fragment brings its own padding', () => {
    const withoutComments = shell.replace(/\/\*[\s\S]*?\*\//g, '')

    expect(withoutComments, 'a box inside a box').not.toMatch(/body \{[^}]*padding:/)
  })

  it('threads the edge inset through both the stylesheet and the script', () => {
    const custom = renderFrameShell({ edgeInset: 20 })

    expect(custom).toContain('calc(100vw - 40px)')
    expect(custom).toContain('var EDGE = 20')
  })

  it('does not break out of its own script element', () => {
    expect(shell.toLowerCase()).not.toContain('</script></script>')
    expect(shell.match(/<script>/g)).toHaveLength(1)
    expect(shell.match(/<\/script>/g)).toHaveLength(1)
  })
})

describe('normalizeArtifactsUrl', () => {
  it('drops trailing slashes', () => {
    expect(normalizeArtifactsUrl('https://acme.dev/docs///')).toBe('https://acme.dev/docs')
  })

  it('assumes https when no scheme is given', () => {
    expect(normalizeArtifactsUrl('acme.dev/docs')).toBe('https://acme.dev/docs')
  })

  it('leaves http alone', () => {
    expect(normalizeArtifactsUrl('http://localhost:8080')).toBe('http://localhost:8080')
  })

  it('returns empty for nothing configured', () => {
    expect(normalizeArtifactsUrl(undefined)).toBe('')
    expect(normalizeArtifactsUrl('   ')).toBe('')
  })

  it('has an auto theme sentinel that is not a real theme', () => {
    expect(DEFAULT_THEMES).not.toContain(AUTO_THEME)
  })
})
