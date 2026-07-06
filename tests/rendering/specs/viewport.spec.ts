import { test, expect, box, ecVisiblePopup, shikiVisiblePopup, galleryCase, expectWithinViewport, expectEcAdjacent } from './helpers.ts'

// An opened popup must lie fully within the visual viewport at mobile and
// tablet widths — a tooltip the reader can't fully see is broken UI.

const VIEWPORTS = [
  { name: 'mobile', width: 390, height: 844 },
  { name: 'tablet', width: 768, height: 1024 },
]

for (const vp of VIEWPORTS) {
  test.describe(`${vp.name} (${vp.width}px)`, () => {
    test.use({ viewport: { width: vp.width, height: vp.height } })

    test('EC: short popup stays within the viewport', async ({ page }) => {
      await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent('ec/local-variables/dark')}&token=0`)
      const popup = ecVisiblePopup(page)
      await expect(popup).toBeVisible()
      expectWithinViewport(await box(popup), vp, 'EC short popup')
    })

    test('EC: long-signature popup stays within the viewport', async ({ page }) => {
      await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent('ec/long-lines/dark')}&token=2`)
      const popup = ecVisiblePopup(page)
      await expect(popup).toBeVisible()
      expectWithinViewport(await box(popup), vp, 'EC long popup')
      // Clamping must not detach the popup from its token vertically
      const token = galleryCase(page, 'ec/long-lines/dark').locator('.glosharp-hover').nth(2)
      expectEcAdjacent(await box(token), await box(popup), vp)
    })

    test('EC: popup for a token near the right edge stays within the viewport', async ({ page }) => {
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
