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

  return `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<meta name="color-scheme" content="light dark">
<style>
${frameStyles(edgeInset)}
</style>
</head>
<body>
<div id="glosharp-content"></div>
<script>
${frameScript(edgeInset, dark, light)}
</script>
</body>
</html>`
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

    keyFor(code)
      .then(function (key) {
        return fetch(base + '/' + resolveTheme() + '/' + key + '.html');
      })
      .then(function (response) { return response.ok ? response.text() : null; })
      .catch(function () { return null; })
      .then(function (html) {
        if (mine !== generation) return;
        if (html) content.innerHTML = html;
        else showFallback(code, ${JSON.stringify(NOT_PUBLISHED_NOTE)});
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
    var next = event.data && event.data.state;
    if (!next || typeof next !== 'object') return;
    state = {
      content: typeof next.content === 'string' ? next.content : state.content,
      artifacts: typeof next.artifacts === 'string' ? next.artifacts : state.artifacts,
      theme: typeof next.theme === 'string' ? next.theme : state.theme
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
