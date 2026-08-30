import { test, expect, box, type Box } from './helpers.ts'
import type { FrameLocator, Locator, Page } from '@playwright/test'

// The GitBook integration renders snippets inside a sandboxed webframe, which
// changes two things the other specs cannot cover: the fragment arrives by
// postMessage + hash lookup rather than being in the page, and a popup cannot
// paint outside the frame's box — the shell has to ask the host to grow it.
//
// gitbook-frame.html drives the real shell through GitBook's message contract,
// using the same host script the package ships for its own local preview.

/** Slack for sub-pixel iframe layout when comparing a popup to the frame box. */
const FRAME_CONTAINMENT_TOLERANCE_PX = 2

/**
 * How long a frame gets to announce itself and report a height. Longer than the
 * default because the page carries one iframe per fixture and each fetches its
 * artifact from the single-threaded gallery server, so the last frame in a
 * parallel run waits behind every other frame's request.
 */
const FRAME_READY_TIMEOUT_MS = 15000

const PUBLISHED_CASE = 'gitbook-frame/local-variables/dark'
const XML_DOCS_CASE = 'gitbook-frame/xml-docs/dark'
const UNPUBLISHED_CASE = 'gitbook-frame/unpublished/dark'
const NO_ARTIFACTS_CASE = 'gitbook-frame/no-artifacts-url/dark'

function frameElement(page: Page, caseId: string): Locator {
  return page.locator(`iframe[data-case="${caseId}"]`)
}

function frame(page: Page, caseId: string): FrameLocator {
  return page.frameLocator(`iframe[data-case="${caseId}"]`)
}

async function gotoFrames(page: Page, path = '/gitbook-frame.html'): Promise<void> {
  await page.goto(`${path}?static`)
}

/** Wait until the host has answered the frame's ready signal and sized it. */
async function settled(page: Page, caseId: string): Promise<void> {
  const element = frameElement(page, caseId)
  await expect(element).toHaveAttribute('data-ready', 'true', { timeout: FRAME_READY_TIMEOUT_MS })
  await expect(element).toHaveAttribute('data-height', /\d+/, { timeout: FRAME_READY_TIMEOUT_MS })
}

function contains(outer: Box, inner: Box, label: string): void {
  const slack = FRAME_CONTAINMENT_TOLERANCE_PX
  expect(inner.x, `${label}: left edge inside frame`).toBeGreaterThanOrEqual(outer.x - slack)
  expect(inner.y, `${label}: top edge inside frame`).toBeGreaterThanOrEqual(outer.y - slack)
  expect(inner.x + inner.width, `${label}: right edge inside frame`).toBeLessThanOrEqual(
    outer.x + outer.width + slack,
  )
  expect(inner.y + inner.height, `${label}: bottom edge inside frame`).toBeLessThanOrEqual(
    outer.y + outer.height + slack,
  )
}

/** How far `inner` pokes out of `outer` on its worst side, in px. */
function overflowOf(outer: Box, inner: Box): number {
  return Math.max(
    0,
    outer.x - inner.x,
    outer.y - inner.y,
    inner.x + inner.width - (outer.x + outer.width),
    inner.y + inner.height - (outer.y + outer.height),
  )
}

function height(element: Locator): Promise<number> {
  return element.getAttribute('data-height').then((v) => Number(v))
}

test('the shell announces itself and every frame gets sized', async ({ page }) => {
  await gotoFrames(page)

  const frames = page.locator('iframe[data-case]')
  const count = await frames.count()
  expect(count, 'the gallery publishes frame cases').toBeGreaterThan(2)

  for (let i = 0; i < count; i++) {
    const element = frames.nth(i)
    const caseId = await element.getAttribute('data-case')
    await expect(element, `${caseId} was answered`).toHaveAttribute('data-ready', 'true', {
      timeout: FRAME_READY_TIMEOUT_MS,
    })
    await expect(element, `${caseId} reported a height`).toHaveAttribute('data-height', /\d+/, {
      timeout: FRAME_READY_TIMEOUT_MS,
    })
  }
})

test('a published snippet is looked up by its own hash and rendered', async ({ page }) => {
  await gotoFrames(page)
  await settled(page, PUBLISHED_CASE)

  const content = frame(page, PUBLISHED_CASE).locator('#glosharp-content')
  await expect(content.locator('.glosharp-hover').first()).toBeVisible()
  await expect(content.locator('.glosharp-frame-fallback')).toHaveCount(0)
})

test('the reported height tracks the rendered snippet, not the placeholder', async ({ page }) => {
  await gotoFrames(page)
  await settled(page, PUBLISHED_CASE)

  const element = frameElement(page, PUBLISHED_CASE)
  const reported = Number(await element.getAttribute('data-height'))
  const rendered = await box(frame(page, PUBLISHED_CASE).locator('#glosharp-content'))

  expect(reported, 'height covers the rendered fragment').toBeGreaterThanOrEqual(rendered.height)
  expect(reported, 'height is not wildly larger than the fragment').toBeLessThan(
    rendered.height + 80,
  )
})

test('hovering a token opens a popup that stays inside the frame', async ({ page }) => {
  await gotoFrames(page)
  await settled(page, PUBLISHED_CASE)

  const token = frame(page, PUBLISHED_CASE).locator('.glosharp-hover').first()
  await token.hover()

  const popup = frame(page, PUBLISHED_CASE).locator('.glosharp-popup:visible').first()
  await expect(popup).toBeVisible()

  // The host applies the shell's resize requests, so re-read the frame box
  // after the popup is up.
  await expect
    .poll(async () => (await box(popup)).height, { message: 'popup laid out' })
    .toBeGreaterThan(0)

  contains(await box(frameElement(page, PUBLISHED_CASE)), await box(popup), 'popup in frame')
})

/**
 * Hover every token in a case, asserting each popup ends up fully inside the
 * frame *and* below the token it belongs to. Returns the index of the first
 * token whose popup only fitted because the frame grew — the case that would be
 * clipped without the shell.
 *
 * Containment on its own is not enough: a popup taller than the space below its
 * token is slid upward by the browser to stay in the viewport, which is
 * perfectly "contained" while sitting on top of the code it explains.
 */
async function hoverEveryToken(page: Page, caseId: string): Promise<number> {
  const element = frameElement(page, caseId)
  const resting = await height(element)
  const tokens = frame(page, caseId).locator('.glosharp-hover')
  const count = await tokens.count()
  expect(count, `${caseId} has hover tokens`).toBeGreaterThan(0)

  let firstGrown = -1
  for (let i = 0; i < count; i++) {
    const token = tokens.nth(i)
    await token.hover()
    const popup = frame(page, caseId).locator('.glosharp-popup:visible').first()
    if ((await popup.count()) === 0) continue

    await expect
      .poll(async () => overflowOf(await box(element), await box(popup)), {
        message: `${caseId} token ${i}: popup fully inside the frame`,
      })
      .toBeLessThanOrEqual(FRAME_CONTAINMENT_TOLERANCE_PX)

    const tokenBox = await box(token)
    const popupBox = await box(popup)
    expect(
      popupBox.y,
      `${caseId} token ${i}: popup opens below its token, not over the code`,
    ).toBeGreaterThanOrEqual(tokenBox.y + tokenBox.height - FRAME_CONTAINMENT_TOLERANCE_PX)

    if (firstGrown < 0 && (await height(element)) > resting) firstGrown = i
  }

  return firstGrown
}

test('every popup ends up inside the frame, growing it when needed', async ({ page }) => {
  await gotoFrames(page)
  await settled(page, XML_DOCS_CASE)

  const grown = await hoverEveryToken(page, XML_DOCS_CASE)

  expect(grown, 'at least one popup needed more room than the resting frame').toBeGreaterThanOrEqual(
    0,
  )
})

test('the grown frame stays grown while the pointer stays on the token', async ({ page }) => {
  // The host resizing us in response to our own resize request fires a resize
  // event inside the frame. Re-measuring on that would collapse the frame right
  // back down and clip the popup that is still open.
  await gotoFrames(page)
  await settled(page, XML_DOCS_CASE)

  const element = frameElement(page, XML_DOCS_CASE)
  const resting = await height(element)
  const grown = await hoverEveryToken(page, XML_DOCS_CASE)
  expect(grown, 'a token whose popup needs extra room').toBeGreaterThanOrEqual(0)

  const token = frame(page, XML_DOCS_CASE).locator('.glosharp-hover').nth(grown)
  await token.hover()
  await expect.poll(() => height(element)).toBeGreaterThan(resting)

  // Long enough for the shell's own re-measure debounce (100ms) to have fired.
  await page.waitForTimeout(400)

  expect(await height(element), 'frame did not collapse under the open popup').toBeGreaterThan(
    resting,
  )
  contains(
    await box(element),
    await box(frame(page, XML_DOCS_CASE).locator('.glosharp-popup:visible').first()),
    'popup still contained',
  )
})

test('the frame shrinks back once the pointer leaves', async ({ page }) => {
  await gotoFrames(page)
  await settled(page, XML_DOCS_CASE)

  const element = frameElement(page, XML_DOCS_CASE)
  const resting = await height(element)
  const grown = await hoverEveryToken(page, XML_DOCS_CASE)
  expect(grown, 'a token whose popup needs extra room').toBeGreaterThanOrEqual(0)

  await frame(page, XML_DOCS_CASE).locator('.glosharp-hover').nth(grown).hover()
  await expect.poll(() => height(element)).toBeGreaterThan(resting)

  await page.locator('h1').hover()
  await expect
    .poll(() => height(element), { message: 'frame returned to its resting height' })
    .toBe(resting)
})

test('a frame whose host starts listening late is still answered', async ({ page }) => {
  // One announcement is a race the frame always loses against a deferred host
  // script: the message lands on nothing and the frame stays blank forever.
  await gotoFrames(page)

  const answered = await page.evaluate(async () => {
    const iframe = document.createElement('iframe')
    iframe.src = 'gitbook-frame-shell.html'
    iframe.style.cssText = 'width:400px;height:100px;border:0'
    document.body.append(iframe)
    await new Promise((r) => iframe.addEventListener('load', r, { once: true }))
    // Deliberately miss the first announcements.
    await new Promise((r) => setTimeout(r, 600))

    return new Promise<boolean>((resolve) => {
      const timer = setTimeout(() => resolve(false), 3000)
      window.addEventListener('message', function onMessage(event) {
        const action = (event.data as { action?: { action?: string } })?.action
        if (event.source === iframe.contentWindow && action?.action === '@webframe.ready') {
          clearTimeout(timer)
          window.removeEventListener('message', onMessage)
          resolve(true)
        }
      })
    })
  })

  expect(answered, 'the shell re-announces until someone is listening').toBe(true)
})

test.describe('unpublished snippet', () => {
  // A missing artifact is a real 404 — that is the behaviour under test, so the
  // browser's own console error for it is expected here and nowhere else.
  test.use({ consoleErrorAllowlist: [/Failed to load resource.*404/] })

  test('degrades to plain code', async ({ page }) => {
    await gotoFrames(page, '/gitbook-frame-unpublished.html')
    await settled(page, UNPUBLISHED_CASE)

    const content = frame(page, UNPUBLISHED_CASE).locator('#glosharp-content')
    await expect(content.locator('.glosharp-frame-fallback')).toContainText(
      'var neverPublished = 1;',
    )
    await expect(content.locator('.glosharp-frame-note')).toContainText('no rendered snippet')
  })
})

test('an unconfigured artifacts URL says so instead of failing silently', async ({ page }) => {
  await gotoFrames(page)
  await settled(page, NO_ARTIFACTS_CASE)

  const content = frame(page, NO_ARTIFACTS_CASE).locator('#glosharp-content')
  await expect(content.locator('.glosharp-frame-fallback')).toContainText('var x = 1;')
  await expect(content.locator('.glosharp-frame-note')).toContainText('artifacts URL')
})
