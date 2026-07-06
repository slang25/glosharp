import { test, expect, galleryCase } from './helpers.ts'

// Static (persistent `^?`) popups render in normal document flow: they must
// never overlap code lines or each other. The local-variables case carries
// six static popups across three code lines — the densest fixture we have.

test('EC static popups do not overlap code lines or each other', async ({ page }) => {
  await page.goto('/ec-dark.html?static')
  const caseEl = galleryCase(page, 'ec/local-variables/dark')
  await caseEl.scrollIntoViewIfNeeded()

  const staticCount = await caseEl.locator('.glosharp-static').count()
  expect(staticCount, 'fixture has static popups to check').toBeGreaterThanOrEqual(6)

  const overlaps = await caseEl.evaluate((sec) => {
    const boxes = [...sec.querySelectorAll('.ec-line, .glosharp-static')].map((el) => {
      const r = el.getBoundingClientRect()
      return { label: `${el.className}: ${el.textContent!.replace(/\s+/g, ' ').slice(0, 30)}`, x: r.x, y: r.y, w: r.width, h: r.height }
    })
    const found: string[] = []
    const TOLERANCE = 1 // adjacent boxes may share an edge
    for (let i = 0; i < boxes.length; i++) {
      for (let j = i + 1; j < boxes.length; j++) {
        const a = boxes[i], b = boxes[j]
        const yOverlap = Math.min(a.y + a.h, b.y + b.h) - Math.max(a.y, b.y)
        const xOverlap = Math.min(a.x + a.w, b.x + b.w) - Math.max(a.x, b.x)
        if (yOverlap > TOLERANCE && xOverlap > TOLERANCE) {
          found.push(`"${a.label}" overlaps "${b.label}" by ${Math.round(yOverlap)}px`)
        }
      }
    }
    return found
  })

  expect(overlaps, 'no static popup overlaps a code line or another popup').toEqual([])
})
