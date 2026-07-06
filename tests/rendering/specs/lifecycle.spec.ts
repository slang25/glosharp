import { test, expect, ecHover, ecVisiblePopup, shikiHover, shikiVisiblePopup, HIDE_DEADLINE_MS } from './helpers.ts'

// Interaction contract: a popup stays open while the pointer moves from the
// token onto the popup itself (so links/content inside are usable), and closes
// once the pointer has left both.

test('EC popup survives pointer moving onto it, closes after leaving', async ({ page }) => {
  await page.goto('/ec-dark.html?static')
  const token = ecHover(page, 'ec/local-variables/dark').first()
  await token.scrollIntoViewIfNeeded()
  await token.hover()

  const popup = ecVisiblePopup(page)
  await expect(popup).toBeVisible()

  // Move the pointer onto the popup and dwell past the plugin's hide delay
  // (100ms): it must remain open while hovered.
  await popup.hover()
  await page.waitForTimeout(300) // deliberate dwell: asserting stability over time
  await expect(popup).toBeVisible()

  // Leave both token and popup: it must close within the hide deadline.
  await page.mouse.move(2, 2)
  await expect(page.locator('.expressive-code > .glosharp-popup-container:visible')).toHaveCount(0, { timeout: HIDE_DEADLINE_MS })
})

test('Shiki popup survives pointer moving onto it, closes after leaving', async ({ page }) => {
  await page.goto('/shiki-dark.html?static')
  const token = shikiHover(page, 'shiki/local-variables/dark').first()
  await token.scrollIntoViewIfNeeded()
  await token.hover()

  const popup = shikiVisiblePopup(page)
  await expect(popup).toBeVisible()

  await popup.hover()
  await page.waitForTimeout(300) // deliberate dwell: asserting stability over time
  await expect(popup).toBeVisible()

  await page.mouse.move(2, 2)
  await expect(page.locator('.glosharp-popup:visible')).toHaveCount(0, { timeout: HIDE_DEADLINE_MS })
})
