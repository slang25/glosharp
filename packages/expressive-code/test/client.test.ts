// @vitest-environment happy-dom
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { buildPopupJsModule } from '../src/client.js'

// The client module, executed in a DOM. Layout values are all zero in
// happy-dom, so these tests cover behaviour (visibility, focus, listeners),
// not geometry; geometry is covered by the Playwright rendering suite.

// Popups are data (popup-tree.ts format), built by the client on first show
const BLOCK = `
<div class="expressive-code"><figure><pre><code>
  <div class="ec-line"><div class="code">
    <span class="glosharp-hover" tabindex="0" data-glosharp-popup="0">a</span>
    = <span class="glosharp-hover" tabindex="-1" data-glosharp-popup="1">b</span>
    + <span class="glosharp-hover" tabindex="-1" data-glosharp-popup="2">c</span>
  </div></div>
</code></pre><script type="application/json" class="glosharp-popups">${JSON.stringify([
  [['code.glosharp-popup-code', ['span.glosharp-keyword', 'int'], ' a']],
  [['code.glosharp-popup-code', ['span.glosharp-keyword', 'int'], ' b']],
  [['code.glosharp-popup-code', ['span.glosharp-symbol-icon', { title: 'local' }, ['svg', { viewBox: '0 0 16 16' }, ['use', { href: '#glosharp-icon-Local' }]]], ['span.glosharp-keyword', 'int'], ' c']],
])}</script></figure></div>`

const runModule = () => new Function(buildPopupJsModule('<svg><symbol id="glosharp-icon-Local"></symbol></svg>'))()

const tokens = () => Array.from(document.querySelectorAll<HTMLElement>('.glosharp-hover'))
const visiblePopups = () => Array.from(document.querySelectorAll<HTMLElement>('.glosharp-popup-container'))
  .filter(p => p.style.getPropertyValue('display') === 'block')
const key = (target: Element, k: string) => target.dispatchEvent(new KeyboardEvent('keydown', { key: k, bubbles: true }))

beforeAll(() => {
  document.body.innerHTML = BLOCK
  runModule()
})

beforeEach(() => {
  // Close anything left open by the previous test
  document.dispatchEvent(new KeyboardEvent('keydown', { key: 'Escape', bubbles: true }))
  ;(document.activeElement as HTMLElement | null)?.blur?.()
})

describe('popup client module', () => {
  it('injects the icon sprite sheet once', () => {
    runModule()
    expect(document.querySelectorAll('#glosharp-sprites')).toHaveLength(1)
  })

  it('builds a popup from the block data on mouseenter, in the EC root, and removes it after mouseleave', async () => {
    expect(document.querySelectorAll('.glosharp-popup-container')).toHaveLength(0)
    vi.useFakeTimers()
    const [a] = tokens()
    a.dispatchEvent(new MouseEvent('mouseenter'))
    const [popup] = visiblePopups()
    expect(popup.textContent).toBe('int a')
    expect(popup.querySelector('code.glosharp-popup-code > span.glosharp-keyword')!.textContent).toBe('int')
    expect(popup.parentElement!.classList.contains('expressive-code')).toBe(true)
    expect(popup.getAttribute('role')).toBe('tooltip')
    expect(a.getAttribute('aria-describedby')).toBe(popup.id)

    a.dispatchEvent(new MouseEvent('mouseleave'))
    vi.advanceTimersByTime(200)
    vi.useRealTimers()
    expect(visiblePopups()).toHaveLength(0)
    expect(popup.isConnected).toBe(false)
    expect(a.hasAttribute('aria-describedby')).toBe(false)

    // Shown again, the same element is reused
    a.dispatchEvent(new MouseEvent('mouseenter'))
    expect(visiblePopups()).toEqual([popup])
  })

  it('builds SVG icons in the SVG namespace with their attributes', () => {
    const [, , c] = tokens()
    c.dispatchEvent(new MouseEvent('mouseenter'))
    const [popup] = visiblePopups()
    const use = popup.querySelector('.glosharp-symbol-icon svg use')!
    expect(use.namespaceURI).toBe('http://www.w3.org/2000/svg')
    expect(use.getAttribute('href')).toBe('#glosharp-icon-Local')
    expect(popup.querySelector('.glosharp-symbol-icon')!.getAttribute('title')).toBe('local')
  })

  it('shows the popup on keyboard focus and closes it with Escape', () => {
    const [a] = tokens()
    a.focus()
    expect(visiblePopups().map(p => p.textContent)).toEqual(['int a'])
    key(a, 'Escape')
    expect(visiblePopups()).toHaveLength(0)
    expect(document.activeElement).toBe(a) // focus stays on the token
    key(a, 'Enter')
    expect(visiblePopups().map(p => p.textContent)).toEqual(['int a'])
  })

  it('moves between tokens with arrow keys using a roving tabindex', () => {
    const [a, b, c] = tokens()
    a.focus()
    key(a, 'ArrowRight')
    expect(document.activeElement).toBe(b)
    expect(tokens().map(t => t.getAttribute('tabindex'))).toEqual(['-1', '0', '-1'])
    expect(visiblePopups().map(p => p.textContent)).toEqual(['int b'])
    key(b, 'End')
    expect(document.activeElement).toBe(c)
    key(c, 'Home')
    expect(document.activeElement).toBe(a)
    expect(tokens().map(t => t.getAttribute('tabindex'))).toEqual(['0', '-1', '-1'])
  })

  it('hides the popup when focus leaves the token', () => {
    const [a] = tokens()
    a.focus()
    expect(visiblePopups()).toHaveLength(1)
    a.blur()
    expect(visiblePopups()).toHaveLength(0)
  })

  it('does not register duplicate document listeners when executed again (client navigation)', () => {
    const spy = vi.spyOn(document, 'addEventListener')
    runModule()
    runModule()
    expect(spy).not.toHaveBeenCalled()
    spy.mockRestore()

    // Behaviour still works exactly once after re-execution and astro:page-load
    document.dispatchEvent(new Event('astro:page-load'))
    const [, b] = tokens()
    b.dispatchEvent(new MouseEvent('mouseenter'))
    expect(visiblePopups()).toHaveLength(1)
  })

  it('works for code blocks added after load (no re-binding needed)', () => {
    const extra = document.createElement('div')
    extra.innerHTML = BLOCK.replace('["span.glosharp-keyword","int"]," a"', '["span.glosharp-keyword","string"]," late"')
    document.body.appendChild(extra)
    const late = extra.querySelector<HTMLElement>('.glosharp-hover')!
    late.dispatchEvent(new MouseEvent('mouseenter'))
    expect(visiblePopups().map(p => p.textContent)).toEqual(['string late'])
    extra.remove()
  })
})
