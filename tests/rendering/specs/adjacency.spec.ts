import {
  test, expect, box, rectDistance,
  ecHover, ecVisiblePopup, shikiHover, shikiVisiblePopup, supportsAnchorPositioning,
  expectEcAdjacent, ADJACENCY_TOLERANCE_PX, FALLBACK_ADJACENCY_TOLERANCE_PX,
} from './helpers.ts'

// Hovering a token must open its popup next to the token — the popup may
// never float detached from the token that owns it.

for (const theme of ['dark', 'light'] as const) {
  test(`EC popup opens adjacent to its token (${theme})`, async ({ page }) => {
    await page.goto(`/ec-${theme}.html?static`)
    const token = ecHover(page, `ec/local-variables/${theme}`).first()
    await token.scrollIntoViewIfNeeded()
    await token.hover()

    const popup = ecVisiblePopup(page)
    await expect(popup).toBeVisible()
    expectEcAdjacent(await box(token), await box(popup))
  })

  test(`Shiki popup opens adjacent to its token (${theme})`, async ({ page }) => {
    await page.goto(`/shiki-${theme}.html?static`)
    const token = shikiHover(page, `shiki/local-variables/${theme}`).first()
    await token.scrollIntoViewIfNeeded()
    await token.hover()

    const popup = shikiVisiblePopup(page)
    await expect(popup).toBeVisible()

    const anchored = await supportsAnchorPositioning(page)
    // KNOWN FINDING: without CSS Anchor Positioning there is no functional
    // fallback in @glosharp/shiki — see fallback.spec.ts.
    test.fail(!anchored, 'no functional fallback positioning in @glosharp/shiki when CSS Anchor Positioning is unavailable')

    const distance = rectDistance(await box(token), await box(popup))
    const tolerance = anchored ? ADJACENCY_TOLERANCE_PX : FALLBACK_ADJACENCY_TOLERANCE_PX
    expect(distance, 'popup adjacent to token').toBeLessThanOrEqual(tolerance)
  })
}
