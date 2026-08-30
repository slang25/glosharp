## ADDED Requirements

### Requirement: Manifest binds the glosharp fence to a custom block
`gitbook-manifest.yaml` SHALL declare a `snippet` block whose `markdown.codeblock` is `glosharp` and whose `markdown.body` is `content`, so that with Git Sync a ` ```glosharp ` fence imports as the block and an editor-inserted block exports back as that fence. The manifest SHALL NOT claim `csharp` or any other built-in language. It SHALL target `space`, declare no scopes, and pass `gitbook integration check`.

#### Scenario: Fence mapping declared
- **WHEN** the manifest is read
- **THEN** the `snippet` block maps `codeblock: glosharp` to the `content` prop

#### Scenario: Manifest validates
- **WHEN** `gitbook integration check` runs with `GLOSHARP_GITBOOK_ORG` set
- **THEN** it reports no errors

### Requirement: Space configuration for the artifacts host and theme
The manifest SHALL declare space configuration properties `artifactsUrl` (the base URL CI publishes to) and `theme` (one of `auto`, `github-dark`, `github-light`, defaulting to `auto`).

#### Scenario: Configuration declared
- **WHEN** the integration is installed on a space
- **THEN** the space settings offer an artifacts URL and a theme

### Requirement: Artifacts URL normalisation
The package SHALL export `normalizeArtifactsUrl(value)` which trims the value, removes trailing slashes, prefixes `https://` when no scheme is present, leaves `http://` and `https://` intact, and returns the empty string when nothing usable was configured.

#### Scenario: Trailing slashes removed
- **WHEN** `normalizeArtifactsUrl("https://acme.dev/docs///")` is called
- **THEN** it returns `https://acme.dev/docs`

#### Scenario: Scheme assumed
- **WHEN** `normalizeArtifactsUrl("acme.dev/docs")` is called
- **THEN** it returns `https://acme.dev/docs`

#### Scenario: Nothing configured
- **WHEN** `normalizeArtifactsUrl(undefined)` or `normalizeArtifactsUrl("   ")` is called
- **THEN** it returns the empty string

### Requirement: Block renders a code block to editors and a webframe to readers
The `snippet` component SHALL render, inside a `block`: in an editable document context, a `codeblock` bound to the `content` state with `syntax="csharp"`, an `@editor.node.updateProps` content-change action, and the webframe as its footer; otherwise the webframe alone. The webframe's `source.url` SHALL be the integration's public content endpoint carrying the integration version as a cache-busting query parameter, and its `data` SHALL carry the dynamic `content` state, the normalised artifacts URL, and the resolved theme. The component SHALL reject a non-document context and SHALL set a render cache max-age.

#### Scenario: Editing shows the source
- **WHEN** the block renders in an editable document context
- **THEN** a `codeblock` bound to `content` is rendered with the webframe in its footer

#### Scenario: Readers get only the frame
- **WHEN** the block renders in a non-editable document context
- **THEN** only the webframe is rendered

#### Scenario: Theme precedence
- **WHEN** the block has a `theme` prop, the installation has a configured theme, and neither is set
- **THEN** the block prop wins, else the installation's theme, else `auto`

### Requirement: The integration serves a static webframe shell
The integration's `fetch` handler SHALL respond with the frame shell document as `text/html` with a public cache max-age. The shell SHALL be a pure function of its options — identical bytes for every request — and the handler SHALL NOT fetch, proxy, or inline artifacts.

The shell SHALL NOT inset the snippet: the rendered fragment carries its own padding, and adding more draws a visible box inside a box instead of a code block. Its one spacing option is the gap kept between a popup and the frame's edges.

#### Scenario: Shell is stable
- **WHEN** `renderFrameShell()` is called twice with the same options
- **THEN** the two documents are byte-identical

#### Scenario: Shell is a complete document
- **WHEN** the shell is rendered
- **THEN** it starts with `<!DOCTYPE html>`, declares `color-scheme: light dark`, and contains exactly one script element

#### Scenario: The snippet fills the frame
- **WHEN** a fragment is rendered in the frame
- **THEN** the shell adds no padding of its own around it

### Requirement: The shell implements GitBook's webframe contract
The shell SHALL announce `@webframe.ready` on load and SHALL keep re-announcing at a short interval until state arrives (bounded). A single announcement is a race the frame always loses when the host attaches its listener late — a deferred script, a hydrating page — and losing it leaves the frame permanently blank. It SHALL post `@webframe.ready`, accept the block's `data` as `{ state }` over `postMessage`, and report its size with `@webframe.resize` carrying both `aspectRatio` and `height`. A message whose `state` is absent or not an object SHALL be ignored. The reported height SHALL cover the rendered fragment plus the shell's padding, and SHALL be re-measured when the frame's **width** changes — and only then, because the host applying the shell's own resize request also raises a resize event, and re-measuring on that would immediately undo height requested for an open popup.

#### Scenario: Ready then state
- **WHEN** the shell loads and the host answers with a state containing content
- **THEN** the shell renders that content and reports a height

#### Scenario: Height covers the fragment
- **WHEN** a fragment is rendered
- **THEN** the reported height is at least the fragment's height and not more than a padding-sized amount larger

#### Scenario: A late host still gets an announcement
- **WHEN** a host attaches its message listener well after the frame has loaded
- **THEN** the frame is still answered, because it re-announced

#### Scenario: No redundant resize traffic
- **WHEN** the height the shell would report equals the height it last reported
- **THEN** it sends no message

#### Scenario: Height-only resize does not re-measure
- **WHEN** the host applies a grown height in response to the shell's own resize request
- **THEN** the shell does not re-measure, and the grown height survives for as long as the popup is open

### Requirement: Artifact lookup by snippet hash
The shell SHALL resolve the theme (the state's theme, else the reader's `prefers-color-scheme` mapped to the shell's dark/light theme pair), hash the state's content with the canonical form, and fetch `<artifacts>/<theme>/<key>.html`. A successful response SHALL be injected as the frame's content. The shell SHALL re-resolve and re-fetch when the reader's colour scheme changes and the theme is `auto`. Responses arriving for superseded content SHALL be discarded.

#### Scenario: Published snippet rendered
- **WHEN** the state's content hashes to a published artifact
- **THEN** the fragment is rendered and its hover tokens are present

#### Scenario: Stale response ignored
- **WHEN** the content changes while a fetch for the previous content is in flight
- **THEN** the earlier response is discarded

### Requirement: Both empty-lookup paths degrade to readable plain code
When no artifacts URL is configured, the shell SHALL render the snippet as escaped plain code with a note naming the missing configuration, and SHALL NOT attempt a fetch. When the fetch fails or returns a non-OK status, it SHALL render the snippet as escaped plain code with a note that nothing has been published for it yet. Empty content SHALL render nothing.

#### Scenario: No artifacts URL
- **WHEN** the state carries an empty artifacts URL
- **THEN** the code is shown as plain text with a note about setting the artifacts URL

#### Scenario: Nothing published
- **WHEN** the artifact for the content is missing
- **THEN** the code is shown as plain text with a note that no rendered snippet has been published

#### Scenario: Code is escaped
- **WHEN** the snippet contains `<`, `>`, or `&`
- **THEN** those characters are escaped in the fallback output

### Requirement: Popups stay inside the frame
The shell SHALL override the rendered fragment's popups to open downward — including under the `@supports not (anchor-name: --x)` fallback — and while a popup is open SHALL request enough height for it and nudge it back inside the right edge if it would overflow.

The required height SHALL be derived from the popup's **anchor**, not from where the popup currently sits. A popup taller than the space below its token is slid upward by the browser to stay inside the viewport; measuring the popup would therefore report that it already fits, the frame would never grow, and the popup would stay parked on top of the code it is explaining — fully "contained" and completely wrong. It SHALL return to its resting height shortly after the pointer leaves, and SHALL treat the popup itself as part of its hover target so moving onto a popup does not collapse the frame. The rendered fragment SHALL NOT be modified to achieve any of this.

#### Scenario: Popups open downward
- **WHEN** the shell's stylesheet is inspected
- **THEN** `.glosharp-popup` is forced to `position-area: bottom`, and the no-anchor fallback is forced to `top: 100%`

#### Scenario: Popup fully visible
- **WHEN** any hover token in the frame is hovered
- **THEN** the popup's box lies within the frame's box

#### Scenario: Popup opens below its own token
- **WHEN** any hover token in the frame is hovered
- **THEN** the popup's top edge is at or below the token's bottom edge, whatever the popup's height

#### Scenario: Frame grows for a popup that would overflow
- **WHEN** a popup would extend past the frame's resting height
- **THEN** the reported height increases so the popup is fully visible

#### Scenario: Frame returns to rest
- **WHEN** the pointer leaves the token and the popup
- **THEN** the reported height returns to its resting value
