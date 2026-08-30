## MODIFIED Requirements

### Requirement: Render hover popups with CSS anchor positioning
The renderer SHALL wrap hover target tokens in `<span class="glosharp-hover">` elements with unique `anchor-name` styles, and SHALL emit each hover's `<div class="glosharp-popup">` **inside** that span, with a matching `position-anchor` style. Popup content SHALL render the hover `parts` array with theme-colored `<span>` elements, followed by documentation text when `docs` is present.

Anchor names SHALL be prefixed per fragment with a short identifier derived from the rendered code (`--gs<8 hex>-<n>`), and the `.glosharp-code` wrapper SHALL declare `anchor-scope: all`. Anchor names are document-global: two fragments on one page that both numbered from `--th-0` would make every popup anchor to the last fragment's token, wherever that is on the page. Deriving the prefix from the code keeps output byte-deterministic, which committed fixtures and content-addressed artifacts both depend on.

#### Scenario: Single hover popup
- **WHEN** a result has one hover at line 0, character 4 with text `(local variable) int x`
- **THEN** the token at that position is wrapped in `<span class="glosharp-hover" style="anchor-name: --gs<id>-0">` and that span contains a `<div class="glosharp-popup" style="position-anchor: --gs<id>-0">` with the parts rendered as themed spans

#### Scenario: Popup is reachable from its anchor
- **WHEN** HTML is rendered
- **THEN** every `.glosharp-popup` is a descendant of the `.glosharp-hover` whose anchor it references, and none appears after `</code></pre>`

#### Scenario: Multiple hovers with unique anchors
- **WHEN** a result has three hovers
- **THEN** their anchor names share the fragment's prefix and are numbered `-0`, `-1`, `-2`, and each popup references its corresponding anchor

#### Scenario: Anchor names differ between fragments
- **WHEN** two different snippets are rendered
- **THEN** their anchor-name prefixes differ, so both fragments can appear on one page without cross-anchoring

#### Scenario: Anchor names are deterministic
- **WHEN** the same result is rendered twice
- **THEN** the two outputs are byte-identical

### Requirement: CSS popup show/hide via hover
The inline CSS SHALL include rules that show the popup on hover: `.glosharp-hover:hover > .glosharp-popup` and `.glosharp-popup:hover` SHALL set `display: block`. Popups SHALL default to `display: none`. The selector SHALL match the emitted nesting — an adjacent-sibling selector cannot reach a popup that is not a sibling, and a popup that no selector can reach can never be shown.

#### Scenario: Popup hidden by default
- **WHEN** HTML is rendered
- **THEN** the CSS includes `.glosharp-popup { display: none; }` and `.glosharp-hover:hover > .glosharp-popup, .glosharp-popup:hover { display: block; }`

#### Scenario: Hovering a token opens its popup in a browser
- **WHEN** rendered output is loaded in a browser and a hover token is pointed at
- **THEN** that token's popup becomes visible and is positioned adjacent to the token

### Requirement: Code block whitespace is exactly the source's
Line breaks inside the code block SHALL come from the newline characters between line spans and from nothing else: `.glosharp-code .line` SHALL be `display: inline`. Chromium serialises a `display: block` boundary as a newline while Firefox serialises it as nothing, so block-level lines plus real newlines double-space the block and double the newlines Chromium puts on the clipboard, while block-level lines without real newlines copy out of Firefox as a single run-on line. Inline lines plus real newlines is the only combination both browsers lay out and copy correctly.

The renderer SHALL therefore emit exactly the source's newlines inside the code block — in particular a nested popup SHALL NOT be followed by one, or it breaks the line after its hover token.

The cost is accepted: a line-level background (`highlight`, `add`, `remove`) ends with the text rather than spanning the block, matching what the Shiki path already does.

#### Scenario: Newlines match the source
- **WHEN** a result with hovers on several lines is rendered
- **THEN** the markup between `<code>` and `</code></pre>` contains exactly as many newlines as the rendered source

#### Scenario: Lines are single-spaced in a browser
- **WHEN** rendered output is loaded in a browser
- **THEN** the code block's height equals its rendered row count times one line box

#### Scenario: Code copies back out unchanged
- **WHEN** the code block's contents are selected and copied, in Chromium or Firefox
- **THEN** the text is the source lines, one per line, without the hidden popup text

#### Scenario: Line-level styling still applies
- **WHEN** a line carries a highlight or diff class
- **THEN** it still renders its distinguishing background
