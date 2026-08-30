import { describe, it, expect } from 'vitest'
import {
  estimateFrameHeight,
  GITBOOK_HOST_SCRIPT,
  renderDevHost,
  renderDevHostError,
  renderWebframeIframe,
} from '../src/dev-host.js'

const state = { content: 'var x = 42;', artifacts: '/artifacts', theme: 'auto' }

describe('GITBOOK_HOST_SCRIPT', () => {
  it('implements the three messages of the webframe contract', () => {
    expect(GITBOOK_HOST_SCRIPT).toContain("'@webframe.ready'")
    expect(GITBOOK_HOST_SCRIPT).toContain("'@webframe.resize'")
    expect(GITBOOK_HOST_SCRIPT).toContain('postMessage({ state:')
  })

  it('mirrors host state onto the frame for tests to wait on', () => {
    expect(GITBOOK_HOST_SCRIPT).toContain('dataset.ready')
    expect(GITBOOK_HOST_SCRIPT).toContain('dataset.height')
    expect(GITBOOK_HOST_SCRIPT).toContain('dataset.resizes')
  })

  it('ignores messages that are not a webframe action', () => {
    expect(GITBOOK_HOST_SCRIPT).toContain("typeof action.action !== 'string'")
  })
})

describe('renderWebframeIframe', () => {
  it('carries the case id, the frame url, and the state', () => {
    const html = renderWebframeIframe('case-1', state, { frameUrl: '/frame' })

    expect(html).toContain('data-case="case-1"')
    expect(html).toContain('src="/frame"')
    expect(html).toContain('&quot;content&quot;:&quot;var x = 42;&quot;')
  })

  it('escapes state that would otherwise break out of the attribute', () => {
    const html = renderWebframeIframe('x', { ...state, content: '"><script>bad()</script>' }, {
      frameUrl: '/frame',
    })

    expect(html).not.toContain('<script>')
    expect(html).toContain('&quot;')
    expect(html).toContain('&lt;script&gt;')
  })

  it('reserves an estimated height only when asked', () => {
    const withReserve = renderWebframeIframe('x', state, { frameUrl: '/f', reserveHeight: true })
    const without = renderWebframeIframe('x', state, { frameUrl: '/f' })

    expect(withReserve).toContain(`style="height:${estimateFrameHeight(state.content)}px"`)
    expect(without).not.toContain('style=')
  })

  it('accepts a custom class', () => {
    expect(renderWebframeIframe('x', state, { frameUrl: '/f', className: 'gallery' })).toContain(
      'class="gallery"',
    )
  })
})

describe('estimateFrameHeight', () => {
  it('grows with the snippet', () => {
    expect(estimateFrameHeight('a\nb\nc')).toBeGreaterThan(estimateFrameHeight('a'))
  })

  it("leaves room for the snippet's own padding even for one line", () => {
    expect(estimateFrameHeight('a')).toBeGreaterThan(32)
  })

  it('is capped so a huge snippet cannot reserve the whole page', () => {
    expect(estimateFrameHeight('x\n'.repeat(5000))).toBeLessThanOrEqual(900)
  })
})

describe('renderDevHost', () => {
  it('renders one frame per case, with the shared host script', () => {
    const html = renderDevHost({
      cases: [
        { id: 'a', title: 'docs/a.md:3', state },
        { id: 'b', title: 'docs/b.md:9', state },
      ],
      frameUrl: '/frame',
    })

    expect(html.startsWith('<!DOCTYPE html>')).toBe(true)
    expect(html.match(/<iframe /g)).toHaveLength(2)
    expect(html, 'frames start near their real height, so loading does not reflow').toContain(
      `height:${estimateFrameHeight(state.content)}px`,
    )
    expect(html).toContain('docs/a.md:3')
    expect(html).toContain(GITBOOK_HOST_SCRIPT)
  })

  it('says so when there is nothing to preview', () => {
    expect(renderDevHost({ cases: [], frameUrl: '/frame' })).toContain('No <code>glosharp</code>')
  })

  it('escapes titles and summaries', () => {
    const html = renderDevHost({
      cases: [{ id: 'a', title: '<img onerror=x>', state }],
      frameUrl: '/frame',
      summary: '1 & 2',
    })

    expect(html).toContain('&lt;img onerror=x&gt;')
    expect(html).toContain('1 &amp; 2')
  })
})

describe('renderDevHostError', () => {
  it('shows the message and the hint, escaped', () => {
    const html = renderDevHostError('CS1002: <expected>', 'Fix the snippet.')

    expect(html).toContain('CS1002: &lt;expected&gt;')
    expect(html).toContain('Fix the snippet.')
    expect(html).not.toContain('<expected>')
  })
})
