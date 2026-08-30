import {
  test, expect, box, rectDistance, galleryCase, supportsAnchorPositioning,
  expectWithinViewport, ADJACENCY_TOLERANCE_PX, FALLBACK_ADJACENCY_TOLERANCE_PX,
} from './helpers.ts'
import type { Page } from '@playwright/test'

// The standalone renderer (`glosharp render`) ships its markup and its CSS
// together, so nothing outside it can compensate for a mistake in either. It is
// also the output the GitBook integration serves to readers.
//
// These pages are built from committed HTML fixtures, so they exercise the real
// bytes the CLI emits.

function standaloneHover(page: Page, caseId: string) {
  return galleryCase(page, caseId).locator('.glosharp-hover')
}

for (const theme of ['dark', 'light'] as const) {
  test(`standalone popup opens on hover (${theme})`, async ({ page }) => {
    // The regression that motivated this page: popups were emitted after the
    // <pre>, so the selector meant to reveal them matched nothing and no popup
    // could ever open.
    await page.goto(`/standalone-${theme}.html?static`)
    const token = standaloneHover(page, `standalone/local-variables/${theme}`).first()
    await token.scrollIntoViewIfNeeded()

    const popup = page.locator('.glosharp-popup:visible')
    await expect(popup, 'no popup is open before hovering').toHaveCount(0)

    await token.hover()
    await expect(popup.first()).toBeVisible()
  })

  test(`standalone popup opens adjacent to its token (${theme})`, async ({ page }) => {
    await page.goto(`/standalone-${theme}.html?static`)
    const token = standaloneHover(page, `standalone/local-variables/${theme}`).first()
    await token.scrollIntoViewIfNeeded()
    await token.hover()

    const popup = page.locator('.glosharp-popup:visible').first()
    await expect(popup).toBeVisible()

    const anchored = await supportsAnchorPositioning(page)
    const distance = rectDistance(await box(token), await box(popup))
    const tolerance = anchored ? ADJACENCY_TOLERANCE_PX : FALLBACK_ADJACENCY_TOLERANCE_PX
    expect(distance, 'popup adjacent to token').toBeLessThanOrEqual(tolerance)
  })
}

test('the code block is exactly as tall as its line count', async ({ page }) => {
  // Two ways a <pre> quietly mangles code, both invisible to any assertion about
  // popups: a stray newline around a nested popup wraps the line after every
  // hover token, and a line-break source that is counted twice (block spans plus
  // real newlines) double-spaces the whole block. Both show up as a code block
  // taller than its own line count.
  await page.goto('/standalone-dark.html?static')
  const block = galleryCase(page, 'standalone/local-variables/dark')

  const { totalHeight, lineHeight, rows } = await block.evaluate((el) => {
    const code = el.querySelector('pre > code')!
    const lines = [...el.querySelectorAll('.line')]
    const heights = lines.map((l) => l.getBoundingClientRect().height).filter((h) => h > 0)
    // A trailing empty line span comes from the source's final newline and
    // renders no row, which is what you want — no phantom blank line.
    let rows = lines.length
    while (rows > 0 && lines[rows - 1].textContent === '') rows--
    return {
      totalHeight: code.getBoundingClientRect().height,
      // Blank interior lines have no text and so no box of their own; a rendered
      // row is whatever the non-empty lines measure.
      lineHeight: Math.min(...heights),
      rows,
    }
  })

  expect(rows, 'the fixture has several rendered rows').toBeGreaterThan(1)
  expect(totalHeight, 'no row is doubled or wrapped').toBeLessThanOrEqual(rows * lineHeight + 2)
  expect(totalHeight, 'every row still occupies a line').toBeGreaterThanOrEqual(
    rows * lineHeight - 2,
  )
})

test('code copies back out as the original lines', async ({ page }) => {
  await page.goto('/standalone-dark.html?static')
  const copied = await galleryCase(page, 'standalone/local-variables/dark')
    // `pre > code`: the popups nested in the code block have their own <code>.
    .locator('pre > code')
    .evaluate((el) => {
      const range = document.createRange()
      range.selectNodeContents(el)
      const selection = getSelection()!
      selection.removeAllRanges()
      selection.addRange(range)
      return selection.toString()
    })

  expect(copied.split('\n').length, 'one text line per code line').toBeGreaterThan(1)
  expect(copied, 'tokens are not split across lines').toContain('var greeting = "Hello, World!";')
  expect(copied, 'hidden popup text does not come along').not.toContain('(local variable)')
})

test('standalone popup carries the hover text and its documentation', async ({ page }) => {
  await page.goto('/standalone-dark.html?static')
  const token = standaloneHover(page, 'standalone/xml-docs/dark').first()
  await token.scrollIntoViewIfNeeded()
  await token.hover()

  const popup = page.locator('.glosharp-popup:visible').first()
  await expect(popup).toBeVisible()
  await expect(popup.locator('.glosharp-popup-code')).not.toBeEmpty()
})

test('standalone popup closes once the pointer leaves', async ({ page }) => {
  await page.goto('/standalone-dark.html?static')
  const token = standaloneHover(page, 'standalone/local-variables/dark').first()
  await token.scrollIntoViewIfNeeded()
  await token.hover()
  await expect(page.locator('.glosharp-popup:visible').first()).toBeVisible()

  await page.locator('h1').hover()
  await expect(page.locator('.glosharp-popup:visible')).toHaveCount(0)
})

test('line-level styling still renders a background', async ({ page }) => {
  // Inline lines are what make spacing and copying correct in both browsers; the
  // accepted cost is that this band ends with the text instead of spanning the
  // block. Losing it entirely would not be accepted.
  await page.goto('/standalone-dark.html?static')
  const highlighted = galleryCase(page, 'standalone/annotations/dark').locator('.glosharp-highlight')
  await expect(highlighted.first()).toBeAttached()

  const background = await highlighted.first().evaluate((el) => getComputedStyle(el).backgroundColor)
  expect(background, 'a highlighted line is visibly distinguished').not.toBe('rgba(0, 0, 0, 0)')
})

test('standalone output contains no scripts', async ({ page }) => {
  // A hard spec requirement, and the reason the output is safe to inline into
  // a sandboxed webframe.
  await page.goto('/standalone-dark.html?static')
  const scripts = await page.locator('section[data-gallery-case] script').count()
  expect(scripts, 'rendered fragments must contain no script elements').toBe(0)
})

test.describe('mobile', () => {
  test.use({ viewport: { width: 390, height: 844 } })

  test('standalone popup stays within the viewport', async ({ page }) => {
    await page.goto('/standalone-dark.html?static')
    const token = standaloneHover(page, 'standalone/local-variables/dark').first()
    await token.scrollIntoViewIfNeeded()
    await token.hover()

    const popup = page.locator('.glosharp-popup:visible').first()
    await expect(popup).toBeVisible()
    expectWithinViewport(await box(popup), { width: 390, height: 844 }, 'standalone popup')
  })
})
