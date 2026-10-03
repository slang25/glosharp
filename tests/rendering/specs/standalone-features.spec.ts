import { test, expect, box, galleryCase } from './helpers.ts'
import type { Page } from '@playwright/test'
import { readFile } from 'node:fs/promises'

// What `glosharp render` draws beyond plain hovers: diagnostics, persistent
// queries, custom tags, keyboard access, popup placement and CSS isolation.
// Each of these shipped broken in the standalone renderer while the Shiki and
// EC paths had them.

const fixtureHtml = (name: string, theme: string) =>
  readFile(new URL(`../fixtures/html/${name}.${theme}.html`, import.meta.url), 'utf8')

async function scrollTokenTo(page: Page, token: ReturnType<Page['locator']>, viewportY: number) {
  const { y } = await box(token)
  await page.evaluate((dy) => window.scrollBy(0, dy), y - viewportY)
}

test('squiggles are drawn, including a warning on a hovered identifier', async ({ page }) => {
  // `border-bottom: 2px wavy` is invalid CSS and drew nothing. And a warning
  // on an identifier with a hover (CS0219 on `unused`) lost its squiggle to the
  // hover.
  await page.goto('/standalone-dark.html?static')
  const caseEl = galleryCase(page, 'standalone/severities/dark')
  const underlines = caseEl.locator('.glosharp-error-underline')
  expect(await underlines.count()).toBeGreaterThanOrEqual(2)

  for (const el of await underlines.all()) {
    const style = await el.evaluate((n) => {
      const s = getComputedStyle(n)
      return { line: s.textDecorationLine, style: s.textDecorationStyle }
    })
    expect(style).toEqual({ line: 'underline', style: 'wavy' })
  }

  await expect(caseEl.locator('.glosharp-hover .glosharp-error-underline.glosharp-severity-warning')).toHaveCount(1)
})

test('diagnostic messages sit inside the code block, in its font', async ({ page }) => {
  await page.goto('/standalone-light.html?static')
  const caseEl = galleryCase(page, 'standalone/severities/light')
  const message = caseEl.locator('pre .glosharp-error-message').first()
  await expect(message).toBeVisible()

  const [messageFont, codeFont] = await Promise.all([
    message.evaluate((n) => getComputedStyle(n).fontFamily),
    caseEl.locator('pre').evaluate((n) => getComputedStyle(n).fontFamily),
  ])
  expect(messageFont).toBe(codeFont)
})

test('persistent ^? queries and custom tags are visible without hovering', async ({ page }) => {
  await page.goto('/standalone-dark.html?static')

  const queries = galleryCase(page, 'standalone/local-variables/dark').locator('.glosharp-static')
  await expect(queries).toHaveCount(6)
  for (const q of await queries.all()) await expect(q).toBeVisible()

  const tags = galleryCase(page, 'standalone/custom-tags/dark').locator('.glosharp-tag')
  await expect(tags).toHaveCount(4)
  await expect(tags.first()).toContainText('Returns a cached result')
})

test('a hover target can be reached and opened from the keyboard', async ({ page }) => {
  await page.goto('/standalone-dark.html?static')
  const token = galleryCase(page, 'standalone/local-variables/dark').locator('.glosharp-hover').first()
  await token.focus()
  await page.keyboard.press('Shift+Tab')
  await page.keyboard.press('Tab')

  await expect(token).toBeFocused()
  const popup = token.locator('.glosharp-popup')
  await expect(popup).toBeVisible()
  await expect(popup).toHaveAttribute('role', 'tooltip')
  expect(await token.getAttribute('aria-describedby')).toBe(await popup.getAttribute('id'))
  expect(await token.evaluate((n) => getComputedStyle(n).outlineStyle)).not.toBe('none')
})

for (const [where, viewportY] of [['top', 4], ['bottom', 760]] as const) {
  test(`a popup near the ${where} of the viewport does not cover its token`, async ({ page }) => {
    // With `position-area: top` and no fallback, a popup for a token near the
    // top of the viewport slid down over the code it describes.
    await page.setViewportSize({ width: 1280, height: 800 })
    await page.goto('/standalone-dark.html?static')
    const token = galleryCase(page, 'standalone/xml-docs/dark').locator('.glosharp-hover').nth(3)
    await scrollTokenTo(page, token, viewportY)
    await token.hover()

    const popup = token.locator('.glosharp-popup')
    await expect(popup).toBeVisible()
    const t = await box(token)
    const p = await box(popup)
    const covers = p.y < t.y + t.height - 1 && p.y + p.height > t.y + 1
    expect(covers, `popup ${JSON.stringify(p)} covers token ${JSON.stringify(t)}`).toBe(false)
  })
}

test('fragments in different themes share a page without restyling each other', async ({ page }) => {
  // Each fragment carries its own <style>; an unscoped rule in one restyled
  // every other glosharp block (and other renderers' output) on the page.
  const [dark, light] = await Promise.all([
    fixtureHtml('severities', 'github-dark'),
    fixtureHtml('severities', 'github-light'),
  ])
  await page.setContent(`<!DOCTYPE html><html lang="en"><head><title>mix</title></head><body>
    ${dark}
    ${light}
    <ul class="glosharp-completion-list" id="foreign"><li>other renderer</li></ul>
  </body></html>`)

  const backgrounds = await page.locator('.glosharp-code pre').evaluateAll((els) =>
    els.map((el) => getComputedStyle(el).backgroundColor),
  )
  expect(backgrounds).toEqual(['rgb(13, 17, 23)', 'rgb(255, 255, 255)'])

  const foreign = await page.locator('#foreign').evaluate((n) => {
    const s = getComputedStyle(n)
    return { border: s.borderTopStyle, background: s.backgroundColor }
  })
  expect(foreign).toEqual({ border: 'none', background: 'rgba(0, 0, 0, 0)' })
})

test('a fragment keeps its popup placement next to the Shiki stylesheet', async ({ page }) => {
  // The other direction: the Shiki path's global `.glosharp-popup` rules
  // (`position-area: top`, a scrolling max-height) must not leak in.
  const [fragment, shikiCss] = await Promise.all([
    fixtureHtml('local-variables', 'github-dark'),
    readFile(new URL('../../../packages/shiki/src/style.css', import.meta.url), 'utf8'),
  ])
  await page.setContent(`<!DOCTYPE html><html lang="en"><head><title>mix</title>
    <style>${shikiCss}</style></head><body style="padding-top: 300px">${fragment}</body></html>`)

  const token = page.locator('.glosharp-hover').first()
  await token.hover()
  const popup = token.locator('.glosharp-popup')
  await expect(popup).toBeVisible()
  const t = await box(token)
  const p = await box(popup)
  expect(p.y, 'popup opens below its token').toBeGreaterThanOrEqual(t.y + t.height)
  expect(await popup.evaluate((n) => getComputedStyle(n).overflowY)).toBe('visible')
})
