/**
 * GitBook's side of the webframe contract, as a browser script.
 *
 * The whole contract is three messages: the frame announces `@webframe.ready`,
 * the host replies with the block's `data` as `{ state }`, and the host applies
 * every `@webframe.resize` to the frame's height. Implementing exactly that and
 * nothing more is what makes both the local preview and the Playwright suite
 * faithful stand-ins for GitBook.
 *
 * Observable state is mirrored onto data attributes so tests can wait on it.
 */
export const GITBOOK_HOST_SCRIPT = `(function () {
  function frames() {
    return [].slice.call(document.querySelectorAll('iframe[data-case]'));
  }

  window.addEventListener('message', function (event) {
    var action = event.data && event.data.action;
    if (!action || typeof action.action !== 'string') return;

    var frame = null;
    var all = frames();
    for (var i = 0; i < all.length; i++) {
      if (all[i].contentWindow === event.source) frame = all[i];
    }
    if (!frame) return;

    if (action.action === '@webframe.ready') {
      frame.contentWindow.postMessage({ state: JSON.parse(frame.dataset.state) }, '*');
      frame.dataset.ready = 'true';
      return;
    }

    if (action.action === '@webframe.resize') {
      frame.style.height = action.size.height + 'px';
      frame.dataset.height = String(action.size.height);
      frame.dataset.resizes = String(Number(frame.dataset.resizes || 0) + 1);
    }
  });
})();`

/** The `data` a GitBook block hands its webframe. */
export interface WebframeState {
  content: string
  artifacts: string
  theme: string
}

export interface DevHostCase {
  /** Stable identifier, also used as the iframe's `data-case`. */
  id: string
  /** Heading shown above the frame. */
  title?: string
  /** Sub-heading, e.g. the artifact key. */
  detail?: string
  state: WebframeState
}

/**
 * Rough height of a rendered snippet, used only to size the iframe before it
 * reports its real height. A frame that starts at some arbitrary default and
 * then snaps reflows everything below it; starting close means the correction
 * is a few pixels. Deliberately crude — marker lines get stripped before
 * rendering and diagnostics add rows, so this is a starting point, not a claim.
 */
export function estimateFrameHeight(content: string): number {
  const LINE_PX = 15
  const CHROME_PX = 32 // the rendered snippet's own padding (2 × 16)
  const lines = Math.max(1, content.split('\n').length)
  return Math.min(lines * LINE_PX + CHROME_PX, 900)
}

/** One `<iframe>` wired the way GitBook wires a webframe. */
export function renderWebframeIframe(
  id: string,
  state: WebframeState,
  options: { frameUrl: string; className?: string; reserveHeight?: boolean },
): string {
  const attributes = [
    `class="${escapeAttribute(options.className ?? 'webframe')}"`,
    `data-case="${escapeAttribute(id)}"`,
    `src="${escapeAttribute(options.frameUrl)}"`,
    `data-state="${escapeAttribute(JSON.stringify(state))}"`,
  ]
  if (options.reserveHeight) {
    attributes.push(`style="height:${estimateFrameHeight(state.content)}px"`)
  }
  return `<iframe ${attributes.join(' ')}></iframe>`
}

/**
 * A standalone page that shows snippets exactly as the GitBook block would:
 * real shell, real artifact lookup, real resize traffic — without a space.
 */
export function renderDevHost(options: {
  cases: DevHostCase[]
  frameUrl: string
  title?: string
  summary?: string
}): string {
  const title = options.title ?? 'Glo# for GitBook — local preview'
  const body = options.cases.length
    ? options.cases
        .map(
          (item) => `<section class="case">
<h2>${escapeText(item.title ?? item.id)}</h2>
${item.detail ? `<p class="detail">${escapeText(item.detail)}</p>` : ''}
${renderWebframeIframe(item.id, item.state, { frameUrl: options.frameUrl, reserveHeight: true })}
</section>`,
        )
        .join('\n')
    : `<p class="empty">No <code>glosharp</code> fences found.</p>`

  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="viewport" content="width=device-width, initial-scale=1">
<meta name="color-scheme" content="light dark">
<title>${escapeText(title)}</title>
<!-- Before the frames are parsed: a frame that announces itself while the host
     has no listener yet would never be answered. -->
<script>
${GITBOOK_HOST_SCRIPT}
</script>
<style>
:root { color-scheme: light dark; }
body {
  margin: 0 auto;
  padding: 32px 24px 64px;
  max-width: 900px;
  font-family: system-ui, sans-serif;
  line-height: 1.5;
}
h1 { font-size: 20px; margin: 0 0 4px; }
.summary { margin: 0 0 32px; opacity: 0.7; font-size: 13px; }
.case { margin: 0 0 40px; }
.case h2 { font-size: 13px; font-weight: 600; margin: 0; }
.detail {
  margin: 2px 0 8px;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 11px;
  opacity: 0.55;
}
.webframe {
  /* A visible boundary: the point of this page is seeing whether a popup fits
     inside the frame, which is impossible to judge if the edges are invisible. */
  width: 100%;
  display: block;
  height: 120px;
  border: 1px solid color-mix(in srgb, currentColor 15%, transparent);
  border-radius: 6px;
}
.empty { opacity: 0.7; }
.error {
  padding: 12px 16px;
  border-left: 3px solid #d1242f;
  background: rgba(209, 36, 47, 0.08);
  white-space: pre-wrap;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
}
</style>
</head>
<body>
<h1>${escapeText(title)}</h1>
${options.summary ? `<p class="summary">${escapeText(options.summary)}</p>` : ''}
${body}
</body>
</html>`
}

/** A page shown in place of the preview when the build could not run. */
export function renderDevHostError(message: string, hint?: string): string {
  return `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="utf-8">
<meta name="color-scheme" content="light dark">
<title>Glo# for GitBook — build failed</title>
<style>
:root { color-scheme: light dark; }
body { margin: 0 auto; padding: 32px 24px; max-width: 900px; font-family: system-ui, sans-serif; }
h1 { font-size: 20px; }
pre {
  padding: 12px 16px;
  border-left: 3px solid #d1242f;
  background: rgba(209, 36, 47, 0.08);
  white-space: pre-wrap;
  font-family: ui-monospace, SFMono-Regular, Menlo, Consolas, monospace;
  font-size: 12px;
}
</style>
</head>
<body>
<h1>Could not render the snippets</h1>
<pre>${escapeText(message)}</pre>
${hint ? `<p>${escapeText(hint)}</p>` : ''}
<p>Fix it and reload this page.</p>
</body>
</html>`
}

function escapeText(text: string): string {
  return text.replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
}

function escapeAttribute(value: string): string {
  return escapeText(value).replace(/"/g, '&quot;')
}
