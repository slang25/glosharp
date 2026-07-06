import { test, expect, box, rectDistance, shikiHover, shikiVisiblePopup, FALLBACK_ADJACENCY_TOLERANCE_PX } from './helpers.ts'

// The fallback variant page strips all CSS anchor wiring, simulating a browser
// without CSS Anchor Positioning regardless of the running browser. Popups
// must still appear on hover and land near their token — degraded positioning
// is acceptable, a popup rendered somewhere unrelated is not.

test('fallback popup still appears near its token on hover', async ({ page }) => {
  // KNOWN FINDING: @glosharp/shiki's style.css has no `@supports not` fallback
  // for CSS Anchor Positioning — without anchors the fixed-position popup
  // renders thousands of pixels from its token. Marked test.fail() so it
  // flags ("passed unexpectedly") once a real fallback ships.
  test.fail(true, 'no functional fallback positioning in @glosharp/shiki when CSS Anchor Positioning is unavailable')
  await page.goto('/shiki-fallback.html?static')
  const token = shikiHover(page, 'shiki-fallback/local-variables/dark').first()
  await token.scrollIntoViewIfNeeded()
  await token.hover()

  const popup = shikiVisiblePopup(page)
  await expect(popup).toBeVisible()

  const distance = rectDistance(await box(token), await box(popup))
  expect(distance, 'fallback popup within relaxed distance of its token').toBeLessThanOrEqual(FALLBACK_ADJACENCY_TOLERANCE_PX)
})
