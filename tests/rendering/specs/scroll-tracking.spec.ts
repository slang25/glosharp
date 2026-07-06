import { test, expect, box, ecVisiblePopup, galleryCase, expectEcAdjacent, ecHover } from './helpers.ts'

// The EC popup is JS-positioned and must track its token when the code
// container or the page scrolls (regression class of PR #91). Its specified
// behavior when the token's center leaves the container's visible area is to
// hide, so it never floats over unrelated content.
//
// Tests pin the popup open via the gallery's ?pin affordance (which triggers
// the plugin's own mouseenter logic) so no pointer position interferes.

const CASE = 'ec/long-lines/dark'
// Token in the middle of the long line: far enough in to survive small
// scrolls, close enough to scroll out of view for the hide assertion.
const TOKEN = 6

function pre(page: Parameters<typeof galleryCase>[0]) {
  return galleryCase(page, CASE).locator('.expressive-code pre').first()
}

test('EC popup tracks its token during horizontal container scroll', async ({ page }) => {
  await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent(CASE)}&token=${TOKEN}`)
  const popup = ecVisiblePopup(page)
  await expect(popup).toBeVisible()
  const token = ecHover(page, CASE).nth(TOKEN)
  expectEcAdjacent(await box(token), await box(popup))

  await pre(page).evaluate((el) => { el.scrollLeft += 40 })
  await expect(popup).toBeVisible()
  expectEcAdjacent(await box(token), await box(popup))
})

test('EC popup hides when its token scrolls out of the container', async ({ page }) => {
  await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent(CASE)}&token=${TOKEN}`)
  const popup = ecVisiblePopup(page)
  await expect(popup).toBeVisible()

  await pre(page).evaluate((el) => { el.scrollLeft = el.scrollWidth })
  await expect(page.locator('.expressive-code > .glosharp-popup-container:visible')).toHaveCount(0)
})

test('EC popup tracks its token during vertical page scroll', async ({ page }) => {
  await page.goto(`/ec-dark.html?static&pin=${encodeURIComponent(CASE)}&token=${TOKEN}`)
  const popup = ecVisiblePopup(page)
  await expect(popup).toBeVisible()

  await page.evaluate(() => window.scrollBy(0, 80))
  await expect(popup).toBeVisible()
  const token = ecHover(page, CASE).nth(TOKEN)
  expectEcAdjacent(await box(token), await box(popup))
})
