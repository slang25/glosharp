// Gallery-side debug affordances. Runs after the EC plugin's own JS modules
// (script order in the page guarantees this). Published packages contain no
// gallery-specific code; everything test-related lives here.
//
// Query parameters:
//   ?static        disable all animations/transitions (deterministic capture)
//   ?pin=<case-id> force open a hover popup in the given data-gallery-case
//   &token=<n>     which hover token within the case to pin (default 0)
;(function () {
  const params = new URLSearchParams(location.search)

  if (params.has('static')) {
    const style = document.createElement('style')
    style.textContent = '*, *::before, *::after { animation: none !important; transition: none !important; }'
    document.head.appendChild(style)
  }

  const pin = params.get('pin')
  if (pin) {
    const caseEl = document.querySelector(`[data-gallery-case="${CSS.escape(pin)}"]`)
    if (!caseEl) {
      console.warn(`gallery: no case named "${pin}"`)
      return
    }
    const tokenIdx = Number(params.get('token') ?? 0)
    const hovers = caseEl.querySelectorAll('.glosharp-hover')
    const hover = hovers[tokenIdx]
    if (!hover) {
      console.warn(`gallery: case "${pin}" has no hover token #${tokenIdx}`)
      return
    }
    if (hover.querySelector('.glosharp-popup-container')) {
      // EC path: trigger the plugin's own show logic so the shipped
      // positioning code runs exactly as it would on a real mouseenter.
      hover.dispatchEvent(new MouseEvent('mouseenter'))
    } else {
      // Shiki path: popups are CSS :hover gated; force the sibling open.
      const popup = hover.nextElementSibling
      if (popup && popup.classList.contains('glosharp-popup')) {
        popup.style.display = 'block'
      }
    }
    caseEl.scrollIntoView()
  }
})()
