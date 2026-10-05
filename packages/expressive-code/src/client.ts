// Client-side JS for interactive hover popups.
//
// Everything is driven by document-level delegated listeners that are
// registered exactly once per page (guarded by a window flag), so client-side
// navigation (Astro view transitions, SPA routers) and dynamically inserted
// code blocks work without re-binding and without piling up listeners.
//
// Popup content isn't in the page: each block carries it as JSON
// (script.glosharp-popups, see popup-tree.ts) and a token names its entry with
// data-glosharp-popup. A popup is built on first show, appended to the EC root
// while visible (so the scrolling <pre> doesn't clip it), and removed on hide.
//
// Interaction model:
// - Pointer: show on mouseenter of a token, hide shortly after leaving the
//   token or its popup (so the pointer can travel into the popup).
// - Keyboard: each code block is one tab stop (roving tabindex). Focusing a
//   token shows its popup; ArrowLeft/ArrowRight/Home/End move between tokens
//   in the block; Escape closes the popup; Tab leaves the block.
// - Popups are positioned below the token and flip above it when there is
//   not enough room below; they are clamped horizontally to the viewport.
//
// Note: a document-level *capturing* mouseenter listener receives the
// non-bubbling mouseenter of every element, including synthetic events
// dispatched by tests.

export function buildPopupJsModule(spriteSheetHtml: string): string {
  const spriteHtml = JSON.stringify(spriteSheetHtml)
  return `
(function initGloSharpPopups() {
  if (typeof window === 'undefined' || window.__glosharpPopups) return;
  window.__glosharpPopups = true;

  const TIMEOUT_MS = 100;
  const GAP_PX = 6;
  const MARGIN_PX = 8;
  let popupIdCounter = 0;
  let activePopup = null;
  let activeHover = null;
  let activeEcRoot = null;
  let activeScrollParent = null;
  let hideTimeout = null;

  // Find the nearest ancestor that scrolls (the EC <pre> scrolls horizontally).
  function findScrollParent(el) {
    let node = el.parentElement;
    while (node && node !== document.body) {
      const style = getComputedStyle(node);
      if (/(auto|scroll|overlay)/.test(style.overflowX + ' ' + style.overflowY)) return node;
      node = node.parentElement;
    }
    return null;
  }

  // Position the popup next to its token. Recomputed on every show, scroll and
  // resize so it tracks the token when the code block is scrolled.
  function positionPopup() {
    if (!activePopup || !activeHover || !activeEcRoot) return;

    const hoverRect = activeHover.getBoundingClientRect();
    const ecRect = activeEcRoot.getBoundingClientRect();
    let left = hoverRect.left - ecRect.left;

    // Clamp horizontally so the popup never extends past the viewport
    // (the popup is displayed before positioning, so offsetWidth is real).
    const popupWidth = activePopup.offsetWidth;
    const minLeft = MARGIN_PX - ecRect.left;
    const maxLeft = window.innerWidth - MARGIN_PX - popupWidth - ecRect.left;
    left = Math.min(Math.max(left, minLeft), Math.max(minLeft, maxLeft));

    // Prefer below the token; flip above when it would overflow the viewport
    // bottom and there is more room above.
    const popupHeight = activePopup.offsetHeight;
    const spaceBelow = window.innerHeight - hoverRect.bottom - GAP_PX - MARGIN_PX;
    const spaceAbove = hoverRect.top - GAP_PX - MARGIN_PX;
    const above = popupHeight > spaceBelow && spaceAbove > spaceBelow;
    const top = above
      ? hoverRect.top - ecRect.top - GAP_PX - popupHeight
      : hoverRect.bottom - ecRect.top + GAP_PX;
    activePopup.classList.toggle('glosharp-popup-above', above);

    activePopup.style.left = left + 'px';
    activePopup.style.top = top + 'px';

    // Hide the popup when its token is scrolled out of the code block's
    // visible area, so it can't float over unrelated content.
    if (activeScrollParent) {
      const clipRect = activeScrollParent.getBoundingClientRect();
      const tokenCenter = hoverRect.left + hoverRect.width / 2;
      const visible = tokenCenter >= clipRect.left && tokenCenter <= clipRect.right;
      activePopup.style.visibility = visible ? 'visible' : 'hidden';
    }
  }

  // Mirrors popupTreeToHast in popup-tree.ts
  const SVG_NS = 'http://www.w3.org/2000/svg';
  function buildPopupNode(node) {
    if (typeof node === 'string') return document.createTextNode(node);
    const classes = node[0].split('.');
    const tag = classes.shift();
    const el = tag === 'svg' || tag === 'use' || tag === 'path'
      ? document.createElementNS(SVG_NS, tag)
      : document.createElement(tag);
    if (classes.length) el.setAttribute('class', classes.join(' '));
    for (let i = 1; i < node.length; i++) {
      const item = node[i];
      if (item && typeof item === 'object' && !Array.isArray(item)) {
        for (const name in item) el.setAttribute(name, item[name]);
      } else {
        el.appendChild(buildPopupNode(item));
      }
    }
    return el;
  }

  // Parsed popup data per script element, and built popups per token
  const blockData = new WeakMap();
  const builtPopups = new WeakMap();

  function popupDataFor(hoverEl) {
    for (let node = hoverEl.parentElement; node; node = node.parentElement) {
      const script = node.querySelector(':scope > script.glosharp-popups');
      if (!script) continue;
      let data = blockData.get(script);
      if (!data) {
        try { data = JSON.parse(script.textContent || '[]'); } catch { data = []; }
        blockData.set(script, data);
      }
      return data;
    }
    return null;
  }

  function popupOf(hoverEl) {
    let popup = builtPopups.get(hoverEl);
    if (popup) return popup;
    const content = popupDataFor(hoverEl)?.[Number(hoverEl.dataset.glosharpPopup)];
    if (!content) return null;
    popup = document.createElement('div');
    popup.className = 'glosharp-popup-container';
    for (const node of content) popup.appendChild(buildPopupNode(node));
    builtPopups.set(hoverEl, popup);
    return popup;
  }

  function showTooltip(hoverEl) {
    ensureSpriteSheet();
    if (hideTimeout) { clearTimeout(hideTimeout); hideTimeout = null; }
    if (activeHover === hoverEl) return;

    const popup = popupOf(hoverEl);
    if (!popup) return;
    const ecRoot = hoverEl.closest('.expressive-code');
    if (!ecRoot) return;

    if (activePopup) hideTooltip();

    if (!popup.id) popup.id = 'glosharp-popup-' + (++popupIdCounter);
    popup.setAttribute('role', 'tooltip');
    hoverEl.setAttribute('aria-describedby', popup.id);
    hoverEl.classList.add('glosharp-hover-active');

    // In the EC root, so the scrolling <pre> doesn't clip it. Inserting it
    // starts the fade-in animation; positionPopup does the one layout read.
    ecRoot.appendChild(popup);
    popup.style.setProperty('display', 'block', 'important');
    popup.style.visibility = 'visible';

    activePopup = popup;
    activeHover = hoverEl;
    activeEcRoot = ecRoot;
    activeScrollParent = findScrollParent(hoverEl);

    positionPopup();
    if (activeScrollParent) activeScrollParent.addEventListener('scroll', positionPopup, { passive: true });
    window.addEventListener('scroll', positionPopup, { passive: true, capture: true });
    window.addEventListener('resize', positionPopup, { passive: true });
  }

  function hideTooltip() {
    if (hideTimeout) { clearTimeout(hideTimeout); hideTimeout = null; }
    if (!activePopup) return;
    if (activeScrollParent) activeScrollParent.removeEventListener('scroll', positionPopup);
    window.removeEventListener('scroll', positionPopup, { capture: true });
    window.removeEventListener('resize', positionPopup);

    activePopup.remove();
    activePopup.style.visibility = '';
    activePopup.classList.remove('glosharp-popup-above');
    if (activeHover) {
      activeHover.removeAttribute('aria-describedby');
      activeHover.classList.remove('glosharp-hover-active');
    }
    activePopup = null;
    activeHover = null;
    activeEcRoot = null;
    activeScrollParent = null;
  }

  function scheduleHide() {
    if (hideTimeout) clearTimeout(hideTimeout);
    hideTimeout = setTimeout(hideTooltip, TIMEOUT_MS);
  }

  function hoverTarget(target) {
    return target && target.nodeType === 1 && target.classList.contains('glosharp-hover') ? target : null;
  }

  function inActivePopup(target) {
    return !!(activePopup && target && target.nodeType === 1 && activePopup.contains(target));
  }

  // --- Pointer ---------------------------------------------------------------
  document.addEventListener('mouseenter', (e) => {
    const hoverEl = hoverTarget(e.target);
    if (hoverEl) { showTooltip(hoverEl); return; }
    if (e.target === activePopup && hideTimeout) { clearTimeout(hideTimeout); hideTimeout = null; }
  }, true);

  document.addEventListener('mouseleave', (e) => {
    if (!activePopup) return;
    if (e.target === activeHover || e.target === activePopup) {
      // Keyboard focus keeps the popup open
      if (activeHover && activeHover === document.activeElement) return;
      scheduleHide();
    }
  }, true);

  // Tap/click outside the active token and popup closes it (touch devices)
  document.addEventListener('pointerdown', (e) => {
    if (!activePopup) return;
    const t = e.target;
    if (activeHover && activeHover.contains(t)) return;
    if (inActivePopup(t)) return;
    hideTooltip();
  }, true);

  // --- Keyboard --------------------------------------------------------------
  function tokensOf(hoverEl) {
    const block = hoverEl.closest('pre') || hoverEl.closest('.expressive-code');
    return block ? Array.from(block.querySelectorAll('.glosharp-hover[tabindex]')) : [hoverEl];
  }

  function moveFocus(fromEl, toEl) {
    if (!toEl || toEl === fromEl) return;
    fromEl.setAttribute('tabindex', '-1');
    toEl.setAttribute('tabindex', '0');
    toEl.focus();
    toEl.scrollIntoView({ block: 'nearest', inline: 'nearest' });
  }

  document.addEventListener('focusin', (e) => {
    const hoverEl = hoverTarget(e.target);
    if (!hoverEl) return;
    // Roving tabindex: the focused token becomes the block's tab stop
    for (const other of tokensOf(hoverEl)) {
      if (other !== hoverEl && other.getAttribute('tabindex') === '0') other.setAttribute('tabindex', '-1');
    }
    hoverEl.setAttribute('tabindex', '0');
    showTooltip(hoverEl);
  });

  document.addEventListener('focusout', (e) => {
    if (e.target === activeHover && !inActivePopup(e.relatedTarget)) hideTooltip();
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      if (activePopup) {
        hideTooltip();
        e.stopPropagation();
      }
      return;
    }
    const hoverEl = hoverTarget(e.target);
    if (!hoverEl) return;
    const tokens = tokensOf(hoverEl);
    const idx = tokens.indexOf(hoverEl);
    let next = null;
    if (e.key === 'ArrowRight' || e.key === 'ArrowDown') next = tokens[idx + 1];
    else if (e.key === 'ArrowLeft' || e.key === 'ArrowUp') next = tokens[idx - 1];
    else if (e.key === 'Home') next = tokens[0];
    else if (e.key === 'End') next = tokens[tokens.length - 1];
    else if (e.key === 'Enter' || e.key === ' ') {
      // Re-open after Escape without moving focus
      e.preventDefault();
      showTooltip(hoverEl);
      return;
    }
    else return;
    e.preventDefault();
    if (next) moveFocus(hoverEl, next);
  });

  // --- Symbol icon sprite sheet ---------------------------------------------
  function ensureSpriteSheet() {
    if (!document.body || document.getElementById('glosharp-sprites')) return;
    const div = document.createElement('div');
    div.id = 'glosharp-sprites';
    div.setAttribute('aria-hidden', 'true');
    div.innerHTML = ${spriteHtml};
    document.body.prepend(div);
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', ensureSpriteSheet);
  } else {
    ensureSpriteSheet();
  }

  // Client-side navigation replaces <body>: drop stale state and re-add the
  // sprite sheet (static ^? results reference it without any interaction).
  document.addEventListener('astro:after-swap', () => { hideTooltip(); ensureSpriteSheet(); });
  document.addEventListener('astro:page-load', ensureSpriteSheet);
})();
`
}
