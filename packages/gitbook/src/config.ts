/** Theme value meaning "follow the reader's colour scheme". */
export const AUTO_THEME = 'auto'

/**
 * A trailing slash and a missing scheme are the two ways an author gets the
 * artifacts URL slightly wrong, and both would fail every lookup silently.
 * Returns `''` when nothing usable was configured.
 */
export function normalizeArtifactsUrl(value: string | undefined): string {
  const trimmed = (value ?? '').trim().replace(/\/+$/, '')
  if (!trimmed) return ''
  return /^https?:\/\//i.test(trimmed) ? trimmed : `https://${trimmed}`
}
