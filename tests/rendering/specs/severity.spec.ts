import { test, expect, galleryCase } from './helpers.ts'

// The severity-styling spec requires all renderers to use distinct colors per
// diagnostic severity. The severities fixture carries one error (CS0103) and
// one warning (CS0219) so the two message boxes can be compared directly.

async function messageColors(caseEl: ReturnType<typeof galleryCase>) {
  const error = caseEl.locator('.glosharp-error-message.glosharp-severity-error').first()
  const warning = caseEl.locator('.glosharp-error-message.glosharp-severity-warning').first()
  await expect(error).toBeVisible()
  await expect(warning).toBeVisible()
  return {
    error: await error.evaluate((el) => getComputedStyle(el).color),
    warning: await warning.evaluate((el) => getComputedStyle(el).color),
  }
}

test('EC renders warnings in a different color than errors', async ({ page }) => {
  await page.goto('/ec-dark.html?static')
  const colors = await messageColors(galleryCase(page, 'ec/severities/dark'))
  expect(colors.warning, 'warning color differs from error color').not.toBe(colors.error)
})

test('Shiki renders warnings in a different color than errors', async ({ page }) => {
  // KNOWN FINDING: @glosharp/shiki's style.css defines only the red error
  // styling — no .glosharp-severity-warning / -info rules — so warnings render
  // identical to errors, violating the severity-styling spec.
  test.fail(true, '@glosharp/shiki style.css lacks severity-specific colors; warnings render red like errors')
  await page.goto('/shiki-dark.html?static')
  const colors = await messageColors(galleryCase(page, 'shiki/severities/dark'))
  expect(colors.warning, 'warning color differs from error color').not.toBe(colors.error)
})
