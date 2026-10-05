# Expressive Code hover popups ship as data and are built on first hover

Every symbol-bearing token gets a hover (ADR-0006), so popup markup dominated pages: on the rendering gallery, hidden popups were about half of an Expressive Code page's elements and bytes. The plugin now emits each popup once per block as a compact JSON tree in a `script.glosharp-popups` element (identical popups share an entry), tokens name their entry with `data-glosharp-popup`, and the client module, which the plugin already ships (ADR-0010), builds a popup's DOM the first time it opens. One builder produces the tree; the server renders it to HAST for always-visible `^?` results and the client mirrors that conversion. Measured on a 64-snippet page at 4x CPU throttling: half the DOM nodes and HTML, and about 15% less main-thread work during load.

## Considered Options

- `<template>` per popup: no measurable gain, because the parser still builds the template's content.
- Doing the same for the Shiki transformer: deferred. Its popups are CSS-only today, so every site would have to add a client script; it got the cheaper fix instead (uncoloured display parts as plain text, about a third fewer elements).

## Consequences

- Without JavaScript, Expressive Code tokens have no popups (they already needed the client module to be positioned).
- Popup text isn't in the static HTML, so it isn't indexed or found with the browser's find-in-page.
- The `glosharp render` HTML keeps its inline, script-free popups, for the embedding reasons in ADR-0010.
