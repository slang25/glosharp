import { test, expect, box, ecVisiblePopup, shikiVisiblePopup, galleryCase, expectWithinViewport } from './helpers.ts'

// An opened popup must lie fully within the visual viewport at mobile and
// tablet widths — a tooltip the reader can't fully see is broken UI.

const VIEWPORTS = [
  { name: 'mobile', width: 390, height: 844 },
  { name: 'tablet', width: 768, height: 1024 },
]

// KNOWN FINDING: EC popups use `white-space: nowrap; width: max-content` with
// no max-width and no horizontal clamping in positionPopup(), so any popup
// wider than the viewport (most popups with docs are >390px) overflows on
// mobile. The affected assertions below are marked test.fail() — they start
// flagging ("passed unexpectedly") the moment clamping is implemented.
const EC_MOBILE_CLAMPING_FINDING =
  'EC popups are unclamped (no max-width, no horizontal clamping) and overflow 390px viewports'

for (const vp of VIEWPORTS) {
  test.describe(`${vp.name} (${vp.width}px)`, () => {
    test.use({ viewport: { width: vp.width, height: vp.height } })

    test('EC: short popup stays within the viewport', async ({ page }) => {
      test.fail(vp.width === 390, EC_MOBILE_CLAMPING_FINDING)
      await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent('ec/local-variables/dark')}&token=0`)
      const popup = ecVisiblePopup(page)
      await expect(popup).toBeVisible()
      expectWithinViewport(await box(popup), vp, 'EC short popup')
    })

    test('EC: long-signature popup stays within the viewport', async ({ page }) => {
      test.fail(vp.width === 390, EC_MOBILE_CLAMPING_FINDING)
      await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent('ec/long-lines/dark')}&token=2`)
      const popup = ecVisiblePopup(page)
      await expect(popup).toBeVisible()
      expectWithinViewport(await box(popup), vp, 'EC long popup')
    })

    test('EC: popup for a token near the right edge stays within the viewport', async ({ page }) => {
      test.fail(vp.width === 390, EC_MOBILE_CLAMPING_FINDING)
      await page.goto('/ec-dark.html?static')
      const caseEl = galleryCase(page, 'ec/local-variables/dark')
      await caseEl.scrollIntoViewIfNeeded()
      // Pick the right-most hover token that is still visible in the container.
      const token = await caseEl.locator('.glosharp-hover:has(.glosharp-popup-container)').evaluateAll((els) => {
        let best = -1
        let bestRight = -Infinity
        els.forEach((el, i) => {
          const r = el.getBoundingClientRect()
          if (r.right <= window.innerWidth && r.right > bestRight) { bestRight = r.right; best = i }
        })
        return best
      })
      expect(token, 'a visible right-side token exists').toBeGreaterThanOrEqual(0)
      await caseEl.locator('.glosharp-hover:has(.glosharp-popup-container)').nth(token).hover()
      const popup = ecVisiblePopup(page)
      await expect(popup).toBeVisible()
      expectWithinViewport(await box(popup), vp, 'EC right-edge popup')
    })

    test('Shiki: short popup stays within the viewport', async ({ page }) => {
      await page.goto(`/shiki-dark.html?static&pin=${encodeURIComponent('shiki/local-variables/dark')}&token=0`)
      const popup = shikiVisiblePopup(page)
      await expect(popup).toBeVisible()
      expectWithinViewport(await box(popup), vp, 'Shiki short popup')
    })
  })
}
