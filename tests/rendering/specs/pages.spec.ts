import { test, expect } from './helpers.ts'

// Every gallery page loads cleanly (the shared fixture fails the test on any
// console error or uncaught exception) and the pinned webfont is active.
const PAGES = [
  { path: '/shiki-dark.html', minCases: 16 },
  { path: '/shiki-light.html', minCases: 16 },
  { path: '/shiki-fallback.html', minCases: 16 },
  { path: '/ec-dark.html', minCases: 16 },
  { path: '/ec-light.html', minCases: 16 },
  { path: '/index.html', minCases: 0 },
]

for (const { path, minCases } of PAGES) {
  test(`loads cleanly: ${path}`, async ({ page }) => {
    await page.goto(`${path}?static`)
    const cases = await page.locator('section[data-gallery-case]').count()
    expect(cases, `${path} case sections`).toBeGreaterThanOrEqual(minCases)

    const fontLoaded = await page.evaluate(async () => {
      await document.fonts.ready
      return document.fonts.check("12px 'JetBrains Mono'")
    })
    expect(fontLoaded, 'pinned JetBrains Mono webfont must be active').toBe(true)
  })
}
