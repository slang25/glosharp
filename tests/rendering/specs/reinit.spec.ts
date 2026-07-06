import { test, expect, ecHover, ecVisiblePopup } from './helpers.ts'

// Astro view transitions replace the DOM and dispatch astro:page-load; the EC
// plugin must re-bind its hover handlers on the swapped-in content.

test('EC popups work after a simulated Astro view transition', async ({ page }) => {
  await page.goto('/ec-dark.html?static')

  // Sanity: popups work before the swap.
  const before = ecHover(page, 'ec/local-variables/dark').first()
  await before.scrollIntoViewIfNeeded()
  await before.hover()
  await expect(ecVisiblePopup(page)).toBeVisible()
  await page.mouse.move(2, 2)
  await expect(page.locator('.expressive-code > .glosharp-popup-container:visible')).toHaveCount(0)

  // Swap the whole body (drops all element listeners) and announce page load
  // the way Astro's view transitions do.
  await page.evaluate(() => {
    document.body.innerHTML = document.body.innerHTML
    document.dispatchEvent(new Event('astro:page-load'))
  })

  const after = ecHover(page, 'ec/local-variables/dark').first()
  await after.scrollIntoViewIfNeeded()
  await after.hover()
  await expect(ecVisiblePopup(page)).toBeVisible()
})
