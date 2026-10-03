// @vitest-environment happy-dom
import { beforeAll, beforeEach, describe, expect, it, vi } from 'vitest'
import { buildPopupJsModule } from '../src/client.js'

// The client module, executed in a DOM. Layout values are all zero in
// happy-dom, so these tests cover behaviour (visibility, focus, listeners),
// not geometry; geometry is covered by the Playwright rendering suite.

const BLOCK = `
<div class="expressive-code"><figure><pre><code>
  <div class="ec-line"><div class="code">
    <span class="glosharp-hover" tabindex="0">a<div class="glosharp-popup-container"><code>int a</code></div></span>
    = <span class="glosharp-hover" tabindex="-1">b<div class="glosharp-popup-container"><code>int b</code></div></span>
    + <span class="glosharp-hover" tabindex="-1">c<div class="glosharp-popup-container"><code>int c</code></div></span>
  </div></div>
</code></pre></figure></div>`

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

  it('shows a popup on mouseenter, reparented to the EC root, and hides it after mouseleave', async () => {
    vi.useFakeTimers()
    const [a] = tokens()
    a.dispatchEvent(new MouseEvent('mouseenter'))
    const [popup] = visiblePopups()
    expect(popup.textContent).toBe('int a')
    expect(popup.parentElement!.classList.contains('expressive-code')).toBe(true)
    expect(popup.getAttribute('role')).toBe('tooltip')
    expect(a.getAttribute('aria-describedby')).toBe(popup.id)

    a.dispatchEvent(new MouseEvent('mouseleave'))
    vi.advanceTimersByTime(200)
    vi.useRealTimers()
    expect(visiblePopups()).toHaveLength(0)
    expect(popup.parentElement).toBe(a) // moved back into its token
    expect(a.hasAttribute('aria-describedby')).toBe(false)
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
    extra.innerHTML = BLOCK.replace(/int a/, 'string late')
    document.body.appendChild(extra)
    const late = extra.querySelector<HTMLElement>('.glosharp-hover')!
    late.dispatchEvent(new MouseEvent('mouseenter'))
    expect(visiblePopups().map(p => p.textContent)).toEqual(['string late'])
    extra.remove()
  })
})
