import {
  PluginStyleSettings,
  ensureColorContrastOnBackground,
  mix,
  onBackground,
  setAlpha,
  type ExpressiveCodeTheme,
  type ResolverContext,
  type StyleResolverFn,
} from '@expressive-code/core'

/**
 * Style settings registered with Expressive Code under the `glosharp` key.
 *
 * Every value is resolved per EC theme, so the generated CSS variables follow
 * EC's own theme switching (`themeCssSelector`, `useDarkModeMediaQuery`).
 * Users can override any of them via EC's `styleOverrides.glosharp`.
 */
export interface GloSharpStyleSettings {
  /** Background of hover popups, static `^?` results and completion lists. */
  popupBackground: string
  popupForeground: string
  /** Secondary text in popups (section labels, completion kinds/details). */
  popupMutedForeground: string
  popupBorder: string
  /** Background tint of a directly hovered/focused token. */
  tokenHoverBackground: string
  /** Outline colour of a keyboard-focused token. */
  tokenFocusOutline: string
  errorColor: string
  errorBackground: string
  warningColor: string
  warningBackground: string
  infoColor: string
  infoBackground: string
  highlightBackground: string
  focusDimOpacity: string
  diffAddBackground: string
  diffAddBorder: string
  diffRemoveBackground: string
  diffRemoveBorder: string
  tagLogColor: string
  tagLogBackground: string
  tagWarnColor: string
  tagWarnBackground: string
  tagErrorColor: string
  tagErrorBackground: string
  tagAnnotateColor: string
  tagAnnotateBackground: string
  /** Syntax colours used for type signatures inside popups. */
  syntaxKeyword: string
  syntaxType: string
  syntaxMethod: string
  syntaxProperty: string
  syntaxVariable: string
}

declare module '@expressive-code/core' {
  export interface StyleSettings {
    glosharp: GloSharpStyleSettings
  }
}

type Fallback = { dark: string; light: string }

function themeColor(theme: ExpressiveCodeTheme, keys: string[], fallback: Fallback): string {
  for (const key of keys) {
    const value = theme.colors[key]
    if (value) return value
  }
  return theme.type === 'light' ? fallback.light : fallback.dark
}

/**
 * Finds the foreground a theme assigns to the most specific of the given
 * TextMate scopes (simple prefix matching, like VS Code's scope selectors).
 */
function tokenColor(theme: ExpressiveCodeTheme, scopes: string[]): string | undefined {
  for (const scope of scopes) {
    let best: { length: number; color: string } | undefined
    for (const setting of theme.settings) {
      const color = setting.settings?.foreground
      if (!color || !setting.scope) continue
      for (const selector of setting.scope) {
        const s = selector.trim()
        // Ignore descendant selectors ("a b") and exclusions; they need context we don't have
        if (/\s/.test(s)) continue
        if ((scope === s || scope.startsWith(s + '.')) && (!best || s.length > best.length)) {
          best = { length: s.length, color }
        }
      }
    }
    if (best) return best.color
  }
  return undefined
}

/** Popup background as an opaque colour, so contrast can be computed against it. */
function popupBackground(theme: ExpressiveCodeTheme): string {
  const bg = themeColor(theme, ['editorHoverWidget.background', 'editorWidget.background'], { dark: '#1e1e1e', light: '#f3f3f3' })
  return onBackground(bg, theme.bg)
}

function popupForeground(theme: ExpressiveCodeTheme): string {
  const fg = themeColor(theme, ['editorHoverWidget.foreground', 'editorWidget.foreground', 'editor.foreground'], { dark: theme.fg, light: theme.fg })
  return ensureColorContrastOnBackground(fg, popupBackground(theme), 7)
}

function syntax(scopes: string[], fallback: Fallback): StyleResolverFn {
  return ({ theme }) => {
    const color = tokenColor(theme, scopes) ?? (theme.type === 'light' ? fallback.light : fallback.dark)
    return ensureColorContrastOnBackground(color, popupBackground(theme), 4.5)
  }
}

/** A status colour (error/warning/…) readable as text on its own tinted background. */
function status(keys: string[], fallback: Fallback): StyleResolverFn {
  return ({ theme }) => {
    const color = themeColor(theme, keys, fallback)
    const tintedBg = onBackground(setAlpha(color, 0.12), theme.bg)
    return ensureColorContrastOnBackground(color, tintedBg, 4.5)
  }
}

function tint(setting: keyof GloSharpStyleSettings, alpha = 0.12): StyleResolverFn {
  return ({ resolveSetting }) => setAlpha(resolveSetting(`glosharp.${setting}`), alpha)
}

export const glosharpStyleSettings = new PluginStyleSettings({
  defaultValues: {
    glosharp: {
      popupBackground: ({ theme }) => popupBackground(theme),
      popupForeground: ({ theme }) => popupForeground(theme),
      popupMutedForeground: ({ theme }) =>
        ensureColorContrastOnBackground(mix(popupForeground(theme), popupBackground(theme), 0.3), popupBackground(theme), 4.5),
      popupBorder: ({ theme }) =>
        themeColor(theme, ['editorHoverWidget.border', 'editorWidget.border', 'widget.border'], {
          dark: mix(popupBackground(theme), '#ffffff', 0.2),
          light: mix(popupBackground(theme), '#000000', 0.2),
        }),
      tokenHoverBackground: ['rgba(139, 92, 246, 0.16)', 'rgba(139, 92, 246, 0.12)'],
      tokenFocusOutline: ({ theme }) => themeColor(theme, ['focusBorder'], { dark: '#8b5cf6', light: '#6d28d9' }),
      errorColor: status(['editorError.foreground', 'errorForeground'], { dark: '#f85149', light: '#cf222e' }),
      errorBackground: tint('errorColor'),
      warningColor: status(['editorWarning.foreground'], { dark: '#d29922', light: '#9a6700' }),
      warningBackground: tint('warningColor'),
      infoColor: status(['editorInfo.foreground'], { dark: '#539bf5', light: '#0969da' }),
      infoBackground: tint('infoColor'),
      highlightBackground: ['rgba(173, 124, 255, 0.15)', 'rgba(139, 90, 230, 0.12)'],
      focusDimOpacity: '0.45',
      diffAddBorder: status(['editorGutter.addedBackground', 'gitDecoration.addedResourceForeground'], { dark: '#2ea043', light: '#1a7f37' }),
      diffAddBackground: tint('diffAddBorder', 0.15),
      diffRemoveBorder: status(['editorGutter.deletedBackground', 'gitDecoration.deletedResourceForeground'], { dark: '#f85149', light: '#cf222e' }),
      diffRemoveBackground: tint('diffRemoveBorder', 0.15),
      tagLogColor: status(['editorInfo.foreground'], { dark: '#539bf5', light: '#0969da' }),
      tagLogBackground: tint('tagLogColor', 0.1),
      tagWarnColor: status(['editorWarning.foreground'], { dark: '#d29922', light: '#9a6700' }),
      tagWarnBackground: tint('tagWarnColor', 0.1),
      tagErrorColor: status(['editorError.foreground', 'errorForeground'], { dark: '#f85149', light: '#cf222e' }),
      tagErrorBackground: tint('tagErrorColor', 0.1),
      tagAnnotateColor: status([], { dark: '#b180d7', light: '#8250df' }),
      tagAnnotateBackground: tint('tagAnnotateColor', 0.1),
      syntaxKeyword: syntax(['keyword.type.cs', 'storage.type.cs', 'keyword', 'storage.type'], { dark: '#569cd6', light: '#0000ff' }),
      syntaxType: syntax(['entity.name.type.class.cs', 'entity.name.type', 'support.class', 'support.type'], { dark: '#4ec9b0', light: '#267f99' }),
      syntaxMethod: syntax(['entity.name.function.cs', 'entity.name.function', 'support.function'], { dark: '#dcdcaa', light: '#795e26' }),
      syntaxProperty: syntax(['variable.other.object.property.cs', 'variable.other.property', 'variable.other.object', 'variable.other', 'variable'], { dark: '#9cdcfe', light: '#001080' }),
      syntaxVariable: syntax(['variable.other.readwrite.cs', 'variable.parameter', 'variable.other.readwrite', 'variable'], { dark: '#9cdcfe', light: '#001080' }),
    },
  },
})

// Roslyn display-part kind → syntax colour setting. Kinds not listed here
// (punctuation, operators, namespaces, text) use the popup foreground.
const partKindSettings: Record<string, keyof GloSharpStyleSettings> = {
  keyword: 'syntaxKeyword',
  className: 'syntaxType',
  structName: 'syntaxType',
  interfaceName: 'syntaxType',
  enumName: 'syntaxType',
  delegateName: 'syntaxType',
  typeParameterName: 'syntaxType',
  methodName: 'syntaxMethod',
  propertyName: 'syntaxProperty',
  fieldName: 'syntaxProperty',
  eventName: 'syntaxProperty',
  localName: 'syntaxVariable',
  parameterName: 'syntaxVariable',
}

export function buildBaseStyles({ cssVar }: ResolverContext): string {
  const v = (name: keyof GloSharpStyleSettings) => cssVar(`glosharp.${name}`)

  const partColorRules = Object.entries(partKindSettings)
    .map(([kind, setting]) => `.glosharp-${kind} { color: ${v(setting)}; }`)
    .join('\n')

  const severityRules = (['error', 'warning', 'info'] as const).map(sev => `
.glosharp-error-underline.glosharp-severity-${sev} {
  text-decoration-color: ${v(`${sev}Color`)};
}
.glosharp-error-message.glosharp-severity-${sev} {
  background: ${v(`${sev}Background`)};
  border-inline-start-color: ${v(`${sev}Color`)};
  color: ${v(`${sev}Color`)};
}`).join('\n')

  const tagRules = (['log', 'warn', 'error', 'annotate'] as const).map(tag => {
    const cap = tag[0].toUpperCase() + tag.slice(1)
    return `
.glosharp-tag-${tag} {
  background: ${v(`tag${cap}Background` as keyof GloSharpStyleSettings)};
  border-inline-start-color: ${v(`tag${cap}Color` as keyof GloSharpStyleSettings)};
  color: ${v(`tag${cap}Color` as keyof GloSharpStyleSettings)};
}`
  }).join('\n')

  return `
/* Positioning context for popups reparented to the EC root */
.expressive-code {
  position: relative;
}

/* Hoverable tokens. Only the token under the pointer animates: the
   block-wide underline below switches instantly, because transitioning every
   token in a block costs a style recalc of the whole block per frame
   whenever the pointer enters or leaves it (or scrolls past it). */
.glosharp-hover {
  position: relative;
  border-bottom: 1px dashed transparent;
  transition: background-color 0.15s ease, border-radius 0.15s ease;
}

@media (prefers-reduced-motion: reduce) {
  .glosharp-hover { transition: none !important; }
  .glosharp-popup-container { animation: none !important; }
}

/* Container hover: subtle underline on all hoverable tokens. EC nests these
   styles inside .expressive-code, so the root itself is "&": a leading
   ".expressive-code:hover" would become ".expressive-code :hover", which
   matches a hover on any descendant and restyles every token in the block on
   each pointer move. */
&:hover .glosharp-hover:not(:hover) {
  border-color: color-mix(in srgb, currentColor 40%, transparent);
}

/* Stronger underline + subtle background on direct token hover or keyboard focus */
.glosharp-hover:hover,
.glosharp-hover:focus-visible,
.glosharp-hover.glosharp-hover-active {
  transition: border-color 0.3s ease, background-color 0.15s ease, border-radius 0.15s ease;
  border-bottom-color: currentColor;
  background: ${v('tokenHoverBackground')};
  border-radius: 2px;
}

.glosharp-hover:focus { outline: none; }
.glosharp-hover:focus-visible {
  outline: 2px solid ${v('tokenFocusOutline')};
  outline-offset: 1px;
}

@keyframes glosharpPopupFadeIn {
  from { opacity: 0; transform: translateY(-4px); }
  to { opacity: 1; transform: translateY(0); }
}

/* Popup container (shared by hover popups and static queries) */
.glosharp-popup-container,
.glosharp-static-container,
.glosharp-completion-list {
  border: 1px solid ${v('popupBorder')};
  border-radius: 4px;
  background: ${v('popupBackground')};
  color: ${v('popupForeground')};
}

.glosharp-popup-container {
  position: absolute;
  z-index: 999 !important;
  font-size: 90%;
  white-space: nowrap !important;
  word-break: normal !important;
  overflow-wrap: normal !important;
  width: max-content !important;
  /* Never exceed the viewport: signature and docs sections wrap when constrained */
  max-width: min(560px, calc(100vw - 24px));
  margin-top: 0.5rem;
  text-align: start;
  animation: glosharpPopupFadeIn 0.12s ease-out;
}

/* Arrow caret on hover popup */
.glosharp-popup-container::before {
  content: '';
  position: absolute;
  top: -5px;
  left: 3px;
  width: 8px;
  height: 8px;
  background: ${v('popupBackground')};
  border-top: 1px solid ${v('popupBorder')};
  border-right: 1px solid ${v('popupBorder')};
  transform: rotate(-45deg);
  pointer-events: none;
  display: inline-block;
}

/* Popup flipped above its token (not enough room below) */
.glosharp-popup-container.glosharp-popup-above {
  margin-top: 0;
}
.glosharp-popup-container.glosharp-popup-above::before {
  top: auto;
  bottom: -5px;
  transform: rotate(135deg);
}

/* Block content rendered after a code line (static ^? results, error
   messages, completion lists, @log/@warn/... callouts). The code line itself
   stays an untouched .ec-line inside this wrapper. */
.glosharp-line {
  position: relative;
  display: block;
}

/* The scrolling <pre> is the size reference for block content, so messages
   wrap to the visible width and stay in view when the code scrolls sideways */
pre:has(.glosharp-line-extras) {
  container-type: inline-size;
}

.glosharp-line-extras {
  position: sticky;
  inset-inline-start: 0;
  box-sizing: border-box;
  max-width: 100cqi;
  display: flex;
  flex-direction: column;
  align-items: flex-start;
  gap: 4px;
  padding: 2px ${cssVar('codePaddingInline')} 6px;
  white-space: normal;
  font-family: ${cssVar('codeFontFamily')};
  user-select: none;
  -webkit-user-select: none;
}

.glosharp-static {
  display: block !important;
  position: relative;
  max-width: 100%;
}

.glosharp-static-container {
  display: block !important;
  z-index: 10;
  font-size: 90%;
  white-space: nowrap !important;
  word-break: normal !important;
  overflow-wrap: normal !important;
  width: max-content !important;
  max-width: 100%;
}

.glosharp-popup-code {
  display: block;
  width: 100%;
  max-width: 600px !important;
  padding: 6px 12px;
  font-family: ${cssVar('codeFontFamily')};
  white-space: pre-wrap;
  max-height: 200px;
  overflow: auto;
  box-sizing: border-box;
}

.glosharp-popup-code,
.glosharp-popup-code span {
  white-space: preserve !important;
}

.glosharp-symbol-icon {
  display: inline-flex;
  align-items: center;
  margin-right: 5px;
  vertical-align: middle;
  cursor: default;
}
.glosharp-symbol-icon svg {
  display: block;
}

.glosharp-popup-types {
  padding: 4px 12px 6px;
  border-top: 1px solid ${v('popupBorder')};
  font-size: 0.9em;
  color: ${v('popupMutedForeground')};
}

.glosharp-popup-types-header {
  font-style: italic;
  margin-bottom: 2px;
}

.glosharp-type-annotation {
  padding-left: 12px;
}

.glosharp-popup-docs {
  max-width: 600px;
  padding: 6px 12px;
  border-top: 1px solid ${v('popupBorder')};
  max-height: 200px;
  overflow: auto;
  white-space: normal;
  text-wrap: pretty;
  font-family: system-ui, -apple-system, 'Segoe UI', sans-serif;
  box-sizing: border-box;
}

.glosharp-popup-summary {
  font-style: italic;
}

.glosharp-popup-params,
.glosharp-popup-returns,
.glosharp-popup-remarks,
.glosharp-popup-example,
.glosharp-popup-exceptions {
  margin-top: 4px;
  padding-top: 4px;
  border-top: 1px solid ${v('popupBorder')};
}

.glosharp-popup-section-label {
  font-size: 0.8em;
  color: ${v('popupMutedForeground')};
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 2px;
}

.glosharp-popup-param,
.glosharp-popup-exception {
  display: flex;
  gap: 6px;
  margin: 1px 0;
}

.glosharp-popup-param-name,
.glosharp-popup-exception-type {
  font-family: ${cssVar('codeFontFamily')};
  font-weight: bold;
  white-space: nowrap;
}

.glosharp-popup-example pre {
  margin: 2px 0;
  padding: 4px 6px;
  background: color-mix(in srgb, currentColor 8%, transparent);
  border-radius: 2px;
  font-size: 0.9em;
  font-family: ${cssVar('codeFontFamily')};
  white-space: pre-wrap;
}

/* Error underlines (text-decoration: "wavy" is not a valid border style) */
.glosharp-error-underline {
  text-decoration-line: underline;
  text-decoration-style: wavy;
  text-decoration-thickness: 1px;
  text-decoration-skip-ink: none;
  text-underline-offset: 3px;
}

/* Error messages and tag callouts: block content below the line */
.glosharp-error-message,
.glosharp-tag {
  display: flex;
  align-items: flex-start;
  gap: 8px;
  box-sizing: border-box;
  max-width: 100%;
  padding: 4px 10px;
  border-radius: 4px;
  border-inline-start: 3px solid transparent;
  font-size: 0.85em;
  line-height: 1.4;
  white-space: pre-wrap;
  overflow-wrap: anywhere;
}
${severityRules}

.glosharp-error-code {
  font-weight: bold;
}

a.glosharp-error-code {
  color: inherit;
  text-decoration: underline;
  text-decoration-color: color-mix(in srgb, currentColor 40%, transparent);
  text-underline-offset: 2px;
}

a.glosharp-error-code:hover,
a.glosharp-error-code:focus-visible {
  text-decoration-color: currentColor;
}

/* Completion list */
.glosharp-completion-list {
  display: grid;
  grid-template-columns: max-content max-content minmax(0, 1fr);
  column-gap: 8px;
  list-style: none;
  margin: 0;
  padding: 4px 0;
  font-size: 0.875em;
  max-height: 200px;
  overflow-y: auto;
  max-width: 100%;
  box-sizing: border-box;
}

.glosharp-completion-item {
  /* Columns are shared by all items, so labels line up */
  display: grid;
  grid-column: 1 / -1;
  grid-template-columns: subgrid;
  padding: 2px 8px;
  align-items: baseline;
  white-space: nowrap;
}

.glosharp-completion-kind {
  font-size: 0.8em;
  color: ${v('popupMutedForeground')};
}

.glosharp-completion-label {
  color: ${v('popupForeground')};
}

.glosharp-completion-detail {
  color: ${v('popupMutedForeground')};
  font-size: 0.85em;
  overflow: hidden;
  text-overflow: ellipsis;
}

/* Line-level visual annotations (classes on the .ec-line itself) */
.ec-line.glosharp-highlight {
  background: ${v('highlightBackground')};
}

.ec-line.glosharp-focus-dim {
  opacity: ${v('focusDimOpacity')};
  transition: opacity 0.2s;
}

.ec-line.glosharp-diff-add {
  background: ${v('diffAddBackground')};
  --ecLineBrdCol: ${v('diffAddBorder')};
  --ecGtrBrdWd: 3px;
}

.ec-line.glosharp-diff-remove {
  background: ${v('diffRemoveBackground')};
  --ecLineBrdCol: ${v('diffRemoveBorder')};
  --ecGtrBrdWd: 3px;
}

.glosharp-tag-icon {
  flex-shrink: 0;
  margin-top: 2px;
}

.glosharp-tag-icon svg {
  display: block;
}

.glosharp-tag-title {
  font-weight: bold;
  margin-right: 4px;
}
${tagRules}

${partColorRules}
`
}
