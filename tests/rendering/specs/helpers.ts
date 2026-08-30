import { test as base, expect, type Page, type Locator } from '@playwright/test'

// ---- Tolerance contract ----------------------------------------------------
// These constants encode "how far a popup may be from its token before we call
// it detached". Changing them is a reviewable decision, not a per-test fudge.

/**
 * EC positions popups via JS at token.left / token.bottom + 6px, and the
 * popup carries an 8px CSS margin-top — nominal visual gap is 14px.
 */
export const EC_HORIZONTAL_TOLERANCE_PX = 3
export const EC_VERTICAL_GAP_RANGE_PX = [4, 18] as const

/**
 * Shiki popups are CSS-anchor positioned (position-area: top, may flip-block).
 * The box-to-box distance between popup and token must stay within this.
 */
export const ADJACENCY_TOLERANCE_PX = 16

/**
 * Relaxed profile for environments without CSS Anchor Positioning: the
 * wrapper-relative fallback puts the popup directly below the token
 * (nominal gap 4px), so anything beyond this means it detached.
 */
export const FALLBACK_ADJACENCY_TOLERANCE_PX = 32

/** EC hides its popup 100ms after mouseleave; how long we allow on top. */
export const HIDE_DEADLINE_MS = 1500

// ---- Console cleanliness ----------------------------------------------------
// Every test fails if the page logs a console error or throws an uncaught
// exception at any point during the test.
//
// A spec whose subject *is* a browser-level failure (a deliberate 404, say) can
// widen this with `test.use({ consoleErrorAllowlist: [/…/] })`. Keep those
// patterns narrow: they are holes in the invariant, and the spec that opens one
// must assert the intended behaviour positively.

export const test = base.extend<{ consoleErrorAllowlist: RegExp[] }>({
  consoleErrorAllowlist: [[], { option: true }],
  page: async ({ page, consoleErrorAllowlist }, use) => {
    const errors: string[] = []
    const allowed = (text: string) => consoleErrorAllowlist.some((p) => p.test(text))
    page.on('console', (msg) => {
      if (msg.type() === 'error' && !allowed(msg.text())) errors.push(`console.error: ${msg.text()}`)
    })
    page.on('pageerror', (err) => {
      if (!allowed(err.message)) errors.push(`pageerror: ${err.message}`)
    })
    await use(page)
    expect(errors, 'page must produce no console errors or uncaught exceptions').toEqual([])
  },
})

export { expect }

// ---- Geometry helpers -------------------------------------------------------

export interface Box { x: number; y: number; width: number; height: number }

export async function box(locator: Locator): Promise<Box> {
  const b = await locator.boundingBox()
  expect(b, `element ${locator} must have a bounding box`).not.toBeNull()
  return b!
}

/** Shortest distance between two rectangles (0 when they touch or overlap). */
export function rectDistance(a: Box, b: Box): number {
  const dx = Math.max(a.x - (b.x + b.width), b.x - (a.x + a.width), 0)
  const dy = Math.max(a.y - (b.y + b.height), b.y - (a.y + a.height), 0)
  return Math.hypot(dx, dy)
}

export function expectWithinViewport(b: Box, viewport: { width: number; height: number }, label: string): void {
  expect(b.x, `${label}: left edge inside viewport`).toBeGreaterThanOrEqual(0)
  expect(b.y, `${label}: top edge inside viewport`).toBeGreaterThanOrEqual(0)
  expect(b.x + b.width, `${label}: right edge inside viewport`).toBeLessThanOrEqual(viewport.width)
  expect(b.y + b.height, `${label}: bottom edge inside viewport`).toBeLessThanOrEqual(viewport.height)
}

/**
 * Assert the EC popup sits where the plugin's JS is meant to put it: left
 * aligned with the token, just below it. When a viewport is given, a popup
 * clamped to a viewport edge is also accepted — but only if the token's
 * x-range still intersects the popup's (a detached popup cannot pass).
 */
export function expectEcAdjacent(token: Box, popup: Box, viewport?: { width: number }): void {
  const aligned = Math.abs(popup.x - token.x) <= EC_HORIZONTAL_TOLERANCE_PX
  if (!aligned && viewport) {
    const CLAMP_MARGIN_PX = 8
    const atEdge =
      popup.x <= CLAMP_MARGIN_PX + EC_HORIZONTAL_TOLERANCE_PX ||
      popup.x + popup.width >= viewport.width - CLAMP_MARGIN_PX - EC_HORIZONTAL_TOLERANCE_PX
    const overlapsToken = token.x < popup.x + popup.width && token.x + token.width > popup.x
    expect(atEdge && overlapsToken, 'popup clamped to viewport edge but still spanning its token').toBe(true)
  } else {
    expect(aligned, `popup left (${popup.x}) aligned with token left (${token.x})`).toBe(true)
  }
  const gap = popup.y - (token.y + token.height)
  expect(gap, 'popup sits just below token').toBeGreaterThanOrEqual(EC_VERTICAL_GAP_RANGE_PX[0])
  expect(gap, 'popup sits just below token').toBeLessThanOrEqual(EC_VERTICAL_GAP_RANGE_PX[1])
}

// ---- Locators ----------------------------------------------------------------

export function galleryCase(page: Page, caseId: string): Locator {
  return page.locator(`section[data-gallery-case="${caseId}"]`)
}

/**
 * EC hover tokens. Plain `.glosharp-hover` so indices match the gallery's
 * ?pin=…&token=<n> affordance, which uses the same selector.
 */
export function ecHover(page: Page, caseId: string): Locator {
  return galleryCase(page, caseId).locator('.glosharp-hover')
}

/**
 * The (single) JS-shown EC hover popup. On show it is reparented to be a
 * direct child of the EC root — this distinguishes it from the always-visible
 * static (persistent `^?`) popups, which live inside `.glosharp-static`.
 */
export function ecVisiblePopup(page: Page): Locator {
  return page.locator('.expressive-code > .glosharp-popup-container').locator('visible=true')
}

export function shikiHover(page: Page, caseId: string): Locator {
  return galleryCase(page, caseId).locator('.glosharp-hover')
}

export function shikiVisiblePopup(page: Page): Locator {
  return page.locator('.glosharp-popup:visible')
}

export function supportsAnchorPositioning(page: Page): Promise<boolean> {
  return page.evaluate(() => CSS.supports('anchor-name: --a'))
}
