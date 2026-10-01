import { canonicalizeSnippet } from './snippet-key.js'

/** Themes the artifact builder publishes by default, dark first. */
export const DEFAULT_THEMES = ['github-dark', 'github-light'] as const

/**
 * `canonicalizeSnippet` as source, for the shell script to reuse verbatim.
 * Exported so a test can evaluate this exact text and compare it against the
 * original — a silent divergence would make every artifact lookup miss.
 */
export const CANONICALIZE_SNIPPET_SOURCE = canonicalizeSnippet.toString()

export interface FrameShellOptions {
  /**
   * Space kept between a popup and the frame's edges, in CSS pixels. The
   * snippet itself is *not* inset: the rendered fragment carries its own
   * padding, and adding more would draw a box inside a box.
   */
  edgeInset?: number
  /** Theme used when neither the block nor the installation picks one. */
  themes?: { dark: string; light: string }
}

/**
 * The document served into the GitBook `<webframe>`.
 *
 * It receives the fence body over `postMessage` (GitBook forwards the block's
 * `data`), hashes it, and pulls the matching pre-rendered fragment from the
 * artifacts host published by CI. Everything the frame does that the rendered
 * fragment cannot do itself — theme resolution, height reporting, making room
 * for a popup — lives here, so `glosharp render` output stays script-free.
 *
 * Pure function of its options: the integration serves the same bytes for every
 * request so GitBook and the browser can cache it.
 */
export function renderFrameShell(options: FrameShellOptions = {}): string {
  const edgeInset = options.edgeInset ?? 12
  const dark = options.themes?.dark ?? DEFAULT_THEMES[0]
  const light = options.themes?.light ?? DEFAULT_THEMES[1]
  const script = `\n${frameScript(edgeInset, dark, light)}\n`

  return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta http-equiv="Content-Security-Policy" content="${frameContentSecurityPolicy(script)}">
<meta name="color-scheme" content="light dark">
<style>
${frameStyles(edgeInset)}
</style>
</head>
<body>
<div id="glosharp-content"></div>
<script>${script}</script>
</body>
</html>`
}

/**
 * The shell's own script is the only script allowed to run (by hash), so
 * markup inside a fetched fragment — inline handlers, `javascript:` URLs,
 * injected `<script>` — is inert even if sanitising missed something. Fetches
 * are limited to https hosts (and the page's own origin / localhost for the
 * dev preview); fragments are style + markup only.
 */
export function frameContentSecurityPolicy(script: string): string {
  return [
    "default-src 'none'",
    `script-src 'sha256-${sha256Base64(script)}'`,
    "style-src 'unsafe-inline'",
    "connect-src 'self' https: http://localhost:* http://127.0.0.1:*",
    'img-src data:',
    'font-src data:',
    "base-uri 'none'",
    "form-action 'none'",
  ].join('; ')
}

function frameStyles(edgeInset: number): string {
  return `:root { color-scheme: light dark; }
* { margin: 0; padding: 0; box-sizing: border-box; }
html, body { background: transparent; }
body {
  /* No padding: the fragment brings its own, and the frame should read as the
     code block itself rather than a box containing one. */
  font-family: ui-monospace, SFMono-Regular, "SF Mono", Menlo, Consolas, monospace;
  font-size: 14px;
}
#glosharp-content pre { overflow-x: auto; }

/* A webframe cannot paint outside its own box, so a popup opening upward is
   clipped by the top edge with nowhere to grow. Open downward instead — then
   the shell can grow the frame on hover without moving the anchor (which would
   pull the pointer off the token and flicker). */
.glosharp-popup {
  position-area: bottom !important;
  margin-top: 4px !important;
  margin-bottom: 0 !important;
  max-width: calc(100vw - ${edgeInset * 2}px) !important;
}
@supports not (anchor-name: --x) {
  .glosharp-popup { top: 100% !important; bottom: auto !important; }
}

.glosharp-frame-fallback {
  font-family: inherit;
  font-size: inherit;
  line-height: 1.5;
  white-space: pre;
  overflow-x: auto;
}
.glosharp-frame-note {
  margin-top: 8px;
  font-family: system-ui, sans-serif;
  font-size: 12px;
  opacity: 0.65;
}`
}

const NO_ARTIFACTS_NOTE =
  'Glo#: set the artifacts URL in the integration configuration to show type information.'
const NOT_PUBLISHED_NOTE =
  'Glo#: no rendered snippet published for this code yet — it appears once CI runs.'

function frameScript(edgeInset: number, dark: string, light: string): string {
  return `(function () {
  var EDGE = ${edgeInset}; // breathing room between a popup and the frame edge
  var POPUP_GAP = 4;       // matches the popup's margin-top below its token
  var DARK_THEME = ${JSON.stringify(dark)};
  var LIGHT_THEME = ${JSON.stringify(light)};
  var canonicalizeSnippet = ${CANONICALIZE_SNIPPET_SOURCE};

  var content = document.getElementById('glosharp-content');
  var state = { content: '', artifacts: '', theme: 'auto' };
  var baseHeight = 0;
  var currentHeight = 0;
  var lastWidth = 0;
  var generation = 0;
  var shrinkTimer = null;

  function sendAction(action) {
    window.parent.postMessage({ action: action }, '*');
  }

  function resize(height) {
    var width = document.documentElement.clientWidth || 1;
    var h = Math.max(Math.ceil(height), 1);
    if (h === currentHeight) return;
    currentHeight = h;
    sendAction({ action: '@webframe.resize', size: { aspectRatio: width / h, height: h } });
  }

  function resolveTheme() {
    if (state.theme && state.theme !== 'auto') return state.theme;
    var prefersDark = window.matchMedia && window.matchMedia('(prefers-color-scheme: dark)').matches;
    return prefersDark ? DARK_THEME : LIGHT_THEME;
  }

  function escapeHtml(text) {
    return String(text).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
  }

  function keyFor(code) {
    var bytes = new TextEncoder().encode(canonicalizeSnippet(code));
    return crypto.subtle.digest('SHA-256', bytes).then(function (digest) {
      return Array.prototype.map
        .call(new Uint8Array(digest), function (b) { return ('0' + b.toString(16)).slice(-2); })
        .join('');
    });
  }

  function showFallback(code, note) {
    content.innerHTML =
      '<pre class="glosharp-frame-fallback"><code>' + escapeHtml(code) + '</code></pre>' +
      '<p class="glosharp-frame-note">' + escapeHtml(note) + '</p>';
  }

  // Elements a rendered fragment never contains and that could load or run
  // something; the CSP blocks them too, this keeps the DOM clean.
  var BLOCKED = /^(script|iframe|frame|frameset|object|embed|link|meta|base|form|input|button|textarea|select|template|svg|math)$/i;
  var URL_ATTRIBUTE = /^(href|src|srcset|action|formaction|xlink:href|poster|background)$/i;

  /** Parse a fetched fragment without executing anything, drop active content, and insert it. */
  function insertFragment(html) {
    var doc = new DOMParser().parseFromString(html, 'text/html');
    var walker = doc.body.querySelectorAll('*');
    for (var i = 0; i < walker.length; i++) {
      var el = walker[i];
      if (BLOCKED.test(el.tagName)) { el.remove(); continue; }
      for (var a = el.attributes.length - 1; a >= 0; a--) {
        var attr = el.attributes[a];
        var name = attr.name.toLowerCase();
        if (name.indexOf('on') === 0) el.removeAttribute(attr.name);
        else if (URL_ATTRIBUTE.test(name) && !/^\\s*https:\\/\\//i.test(attr.value)) el.removeAttribute(attr.name);
      }
    }
    content.replaceChildren.apply(content, Array.prototype.slice.call(doc.body.childNodes));
  }

  /** Only built-in-style theme names: they become a URL path segment. */
  function safeTheme(theme) {
    return typeof theme === 'string' && /^[a-z0-9][a-z0-9-]*$/i.test(theme) ? theme : 'auto';
  }

  function measure() {
    lastWidth = document.documentElement.clientWidth;
    baseHeight = Math.ceil(content.getBoundingClientRect().height);
    resize(baseHeight);
  }

  function paint() {
    var mine = ++generation;
    var code = state.content || '';
    if (!code.replace(/\\s/g, '')) {
      content.innerHTML = '';
      measure();
      return;
    }

    var base = String(state.artifacts || '').replace(/\\/+$/, '');
    if (!base) {
      showFallback(code, ${JSON.stringify(NO_ARTIFACTS_NOTE)});
      measure();
      return;
    }

    var lookedUp = '';
    keyFor(code)
      .then(function (key) {
        lookedUp = resolveTheme() + '/' + key + '.html';
        return fetch(base + '/' + resolveTheme() + '/' + key + '.html');
      })
      .then(function (response) { return response.ok ? response.text() : null; })
      .catch(function () { return null; })
      .then(function (html) {
        if (mine !== generation) return;
        if (html) insertFragment(html);
        else {
          // Name the artifact that was looked for, so a key mismatch between
          // CI and GitBook (or a missing theme) can be debugged.
          if (window.console) console.warn('Glo#: no artifact at ' + base + '/' + lookedUp);
          showFallback(code, ${JSON.stringify(NOT_PUBLISHED_NOTE)} + ' (looked for ' + lookedUp.replace(/^([^/]+\\/)([0-9a-f]{12})[0-9a-f]+/, '$1$2…') + ')');
        }
        measure();
      });
  }

  /** The open popup under the pointer, paired with the token it belongs to. */
  function popupFor(target) {
    if (!target || !target.closest) return null;
    var onPopup = target.closest('.glosharp-popup');
    if (onPopup) return { popup: onPopup, anchor: onPopup.closest('.glosharp-hover') };
    var hover = target.closest('.glosharp-hover');
    if (!hover) return null;
    var nested = hover.querySelector('.glosharp-popup');
    if (nested) return { popup: nested, anchor: hover };
    var next = hover.nextElementSibling;
    return next && next.classList.contains('glosharp-popup')
      ? { popup: next, anchor: hover }
      : null;
  }

  /**
   * Make room for an open popup: the frame grows downward, and a popup that
   * would run off the right edge is nudged back inside.
   *
   * The height has to be derived from the *anchor*, not from where the popup
   * currently sits. When a popup is taller than the space below its token the
   * browser slides it up to keep it in the viewport — so measuring the popup
   * would report that it already fits, the frame would never grow, and the
   * popup would stay parked on top of the code it is explaining.
   */
  function growFor(popup, anchor) {
    popup.style.translate = '';
    var rect = popup.getBoundingClientRect();
    if (!rect.height) return;

    var overflowRight = Math.ceil(rect.right - (document.documentElement.clientWidth - EDGE));
    if (overflowRight > 0) popup.style.translate = -overflowRight + 'px 0';

    var below = anchor ? anchor.getBoundingClientRect().bottom + POPUP_GAP : rect.top;
    resize(Math.max(baseHeight, Math.ceil(below + rect.height) + EDGE));
  }

  document.addEventListener('pointerover', function (event) {
    var found = popupFor(event.target);
    if (!found) return;
    clearTimeout(shrinkTimer);
    requestAnimationFrame(function () { growFor(found.popup, found.anchor); });
  });

  document.addEventListener('pointerout', function (event) {
    if (!popupFor(event.target)) return;
    clearTimeout(shrinkTimer);
    shrinkTimer = setTimeout(function () {
      if (currentHeight !== baseHeight) resize(baseHeight);
    }, 80);
  });

  window.addEventListener('message', function (event) {
    // Only the embedding page (GitBook) may drive the frame; any other window
    // that frames or opens the shell is ignored.
    if (event.source !== window.parent || window.parent === window) return;
    var next = event.data && event.data.state;
    if (!next || typeof next !== 'object') return;
    state = {
      content: typeof next.content === 'string' ? next.content : state.content,
      artifacts: typeof next.artifacts === 'string' ? next.artifacts : state.artifacts,
      theme: typeof next.theme === 'string' ? safeTheme(next.theme) : state.theme
    };
    stopAnnouncing();
    paint();
  });

  if (window.matchMedia) {
    window.matchMedia('(prefers-color-scheme: dark)').addEventListener('change', function () {
      if (!state.theme || state.theme === 'auto') paint();
    });
  }

  // Width only. The host resizing us in response to our own @webframe.resize
  // fires this too, and re-measuring then would immediately undo the extra
  // height we just asked for to fit an open popup.
  var remeasureTimer = null;
  window.addEventListener('resize', function () {
    if (document.documentElement.clientWidth === lastWidth) return;
    clearTimeout(remeasureTimer);
    remeasureTimer = setTimeout(measure, 100);
  });

  /**
   * Announce readiness until the host answers with state.
   *
   * A single announcement is a race the frame always loses when the host
   * attaches its listener late (a deferred script, a hydrating page): the
   * message goes nowhere, no state ever arrives, and the frame sits blank
   * forever. Re-announcing costs one postMessage and removes the whole class.
   */
  var readyTimer = null;
  var readyAttempts = 0;

  function announce() {
    if (state.content) return stopAnnouncing();
    if (++readyAttempts > 40) return stopAnnouncing();
    sendAction({ action: '@webframe.ready' });
  }

  function stopAnnouncing() {
    clearInterval(readyTimer);
    readyTimer = null;
  }

  function onLoaded() {
    announce();
    readyTimer = setInterval(announce, 250);
  }

  if (document.readyState !== 'loading') onLoaded();
  else document.addEventListener('DOMContentLoaded', onLoaded);
})();`
}

/**
 * SHA-256 of a string's UTF-8 bytes, base64-encoded — for the CSP script hash.
 * Synchronous and dependency-free because the shell is built at module load
 * inside the GitBook integration's Worker runtime, where neither `node:crypto`
 * nor an `await` on `crypto.subtle` is available at that point.
 */
export function sha256Base64(text: string): string {
  const bytes = new TextEncoder().encode(text)
  const K = new Uint32Array([
    0x428a2f98, 0x71374491, 0xb5c0fbcf, 0xe9b5dba5, 0x3956c25b, 0x59f111f1, 0x923f82a4, 0xab1c5ed5,
    0xd807aa98, 0x12835b01, 0x243185be, 0x550c7dc3, 0x72be5d74, 0x80deb1fe, 0x9bdc06a7, 0xc19bf174,
    0xe49b69c1, 0xefbe4786, 0x0fc19dc6, 0x240ca1cc, 0x2de92c6f, 0x4a7484aa, 0x5cb0a9dc, 0x76f988da,
    0x983e5152, 0xa831c66d, 0xb00327c8, 0xbf597fc7, 0xc6e00bf3, 0xd5a79147, 0x06ca6351, 0x14292967,
    0x27b70a85, 0x2e1b2138, 0x4d2c6dfc, 0x53380d13, 0x650a7354, 0x766a0abb, 0x81c2c92e, 0x92722c85,
    0xa2bfe8a1, 0xa81a664b, 0xc24b8b70, 0xc76c51a3, 0xd192e819, 0xd6990624, 0xf40e3585, 0x106aa070,
    0x19a4c116, 0x1e376c08, 0x2748774c, 0x34b0bcb5, 0x391c0cb3, 0x4ed8aa4a, 0x5b9cca4f, 0x682e6ff3,
    0x748f82ee, 0x78a5636f, 0x84c87814, 0x8cc70208, 0x90befffa, 0xa4506ceb, 0xbef9a3f7, 0xc67178f2,
  ])
  const h = new Uint32Array([0x6a09e667, 0xbb67ae85, 0x3c6ef372, 0xa54ff53a, 0x510e527f, 0x9b05688c, 0x1f83d9ab, 0x5be0cd19])

  // Padding: 0x80, zeros, then the bit length as a 64-bit big-endian integer.
  const length = Math.ceil((bytes.length + 9) / 64) * 64
  const data = new Uint8Array(length)
  data.set(bytes)
  data[bytes.length] = 0x80
  const view = new DataView(data.buffer)
  view.setUint32(length - 8, Math.floor((bytes.length * 8) / 2 ** 32))
  view.setUint32(length - 4, (bytes.length * 8) >>> 0)

  const w = new Uint32Array(64)
  const rotr = (x: number, n: number) => (x >>> n) | (x << (32 - n))
  for (let offset = 0; offset < length; offset += 64) {
    for (let i = 0; i < 16; i++) w[i] = view.getUint32(offset + i * 4)
    for (let i = 16; i < 64; i++) {
      const s0 = rotr(w[i - 15], 7) ^ rotr(w[i - 15], 18) ^ (w[i - 15] >>> 3)
      const s1 = rotr(w[i - 2], 17) ^ rotr(w[i - 2], 19) ^ (w[i - 2] >>> 10)
      w[i] = (w[i - 16] + s0 + w[i - 7] + s1) >>> 0
    }
    let [a, b, c, d, e, f, g, hh] = h
    for (let i = 0; i < 64; i++) {
      const S1 = rotr(e, 6) ^ rotr(e, 11) ^ rotr(e, 25)
      const ch = (e & f) ^ (~e & g)
      const t1 = (hh + S1 + ch + K[i] + w[i]) >>> 0
      const S0 = rotr(a, 2) ^ rotr(a, 13) ^ rotr(a, 22)
      const maj = (a & b) ^ (a & c) ^ (b & c)
      const t2 = (S0 + maj) >>> 0
      hh = g
      g = f
      f = e
      e = (d + t1) >>> 0
      d = c
      c = b
      b = a
      a = (t1 + t2) >>> 0
    }
    h[0] += a
    h[1] += b
    h[2] += c
    h[3] += d
    h[4] += e
    h[5] += f
    h[6] += g
    h[7] += hh
  }

  let binary = ''
  for (const word of h) {
    binary += String.fromCharCode((word >>> 24) & 0xff, (word >>> 16) & 0xff, (word >>> 8) & 0xff, word & 0xff)
  }
  return btoa(binary)
}
