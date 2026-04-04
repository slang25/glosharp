import { createGloSharp, type GloSharpOptions, type GloSharpResult, type GloSharpHover, type GloSharpError, type GloSharpDisplayPart, type GloSharpCompletion, type GloSharpDocComment, type GloSharpDocParam, type GloSharpDocException, type GloSharpHighlight } from '@glosharp/core'
import type { ExpressiveCodeBlock } from '@expressive-code/core'

export interface PluginGloSharpOptions extends GloSharpOptions {
  project?: string
  region?: string
}

// Style settings for theme-aware colors
const styleSettings = {
  popupBackground: { dark: '#1e1e1e', light: '#f3f3f3' },
  popupForeground: { dark: '#d4d4d4', light: '#1e1e1e' },
  popupBorder: { dark: '#3c3c3c', light: '#c8c8c8' },
  errorUnderline: { dark: '#f44747', light: '#e51400' },
  errorBackground: { dark: 'rgba(244, 71, 71, 0.1)', light: 'rgba(229, 20, 0, 0.1)' },
  warningUnderline: { dark: '#d29922', light: '#9a6700' },
  warningBackground: { dark: 'rgba(210, 153, 34, 0.15)', light: 'rgba(154, 103, 0, 0.15)' },
  infoUnderline: { dark: '#539bf5', light: '#0969da' },
  infoBackground: { dark: 'rgba(83, 155, 245, 0.15)', light: 'rgba(9, 105, 218, 0.15)' },
  highlightBackground: { dark: 'rgba(173, 124, 255, 0.15)', light: 'rgba(139, 90, 230, 0.12)' },
  focusDimOpacity: { dark: '0.4', light: '0.4' },
  diffAddBackground: { dark: 'rgba(46, 160, 67, 0.15)', light: 'rgba(46, 160, 67, 0.12)' },
  diffRemoveBackground: { dark: 'rgba(248, 81, 73, 0.15)', light: 'rgba(248, 81, 73, 0.12)' },
  diffAddBorder: { dark: '#2ea043', light: '#2ea043' },
  diffRemoveBorder: { dark: '#f85149', light: '#f85149' },
}

// Part kind colors (VS Code-like)
const partColors: Record<string, { dark: string; light: string }> = {
  keyword: { dark: '#569cd6', light: '#0000ff' },
  className: { dark: '#4ec9b0', light: '#267f99' },
  structName: { dark: '#4ec9b0', light: '#267f99' },
  interfaceName: { dark: '#b8d7a3', light: '#267f99' },
  enumName: { dark: '#b8d7a3', light: '#267f99' },
  delegateName: { dark: '#4ec9b0', light: '#267f99' },
  typeParameterName: { dark: '#b8d7a3', light: '#267f99' },
  methodName: { dark: '#dcdcaa', light: '#795e26' },
  propertyName: { dark: '#9cdcfe', light: '#001080' },
  fieldName: { dark: '#9cdcfe', light: '#001080' },
  eventName: { dark: '#9cdcfe', light: '#001080' },
  localName: { dark: '#9cdcfe', light: '#001080' },
  parameterName: { dark: '#9cdcfe', light: '#001080' },
  namespaceName: { dark: '#d4d4d4', light: '#1e1e1e' },
  punctuation: { dark: '#d4d4d4', light: '#1e1e1e' },
  operator: { dark: '#d4d4d4', light: '#1e1e1e' },
  text: { dark: '#d4d4d4', light: '#1e1e1e' },
}

function buildBaseStyles(): string {
  const partColorRules = Object.entries(partColors)
    .map(([kind, colors]) =>
      `.glosharp-${kind} { color: var(--glosharp-${kind}-dark, ${colors.dark}); }
[data-theme="light"] .glosharp-${kind} { color: var(--glosharp-${kind}-light, ${colors.light}); }`
    )
    .join('\n')

  return `
/* Ensure EC root is positioned for absolute popup placement */
.expressive-code {
  position: relative;
}

/* Hoverable tokens */
.glosharp-hover {
  position: relative;
  border-bottom: 1px dashed transparent;
  transition-timing-function: ease;
  transition: border-color 0.3s, background-color 0.15s, border-radius 0.15s;
}

@media (prefers-reduced-motion: reduce) {
  .glosharp-hover, .glosharp-noline { transition: none !important; }
}

/* Container hover: subtle underline on all hoverable tokens */
.expressive-code:hover .glosharp-hover:not(.glosharp-hover:hover) {
  border-color: color-mix(in srgb, currentColor 40%, transparent);
}

/* Stronger underline + subtle background on direct token hover */
.expressive-code:hover .glosharp-hover:hover {
  border-bottom-color: currentColor;
  background: rgba(139, 92, 246, 0.08);
  border-radius: 2px;
}

@keyframes glosharpPopupFadeIn {
  from { opacity: 0; transform: translateY(-4px); }
  to { opacity: 1; transform: translateY(0); }
}

/* Popup container (shared by hover popups and static queries) */
.glosharp-popup-container {
  position: absolute;
  z-index: 999 !important;
  border: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
  border-radius: 4px;
  background: var(--glosharp-popup-bg, ${styleSettings.popupBackground.dark});
  color: var(--glosharp-popup-fg, ${styleSettings.popupForeground.dark});
  font-size: 90%;
  white-space: nowrap !important;
  word-break: normal !important;
  overflow-wrap: normal !important;
  width: max-content !important;
  margin-top: 0.5rem;
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
  background: var(--glosharp-popup-bg, ${styleSettings.popupBackground.dark});
  border-top: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
  border-right: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
  transform: rotate(-45deg);
  pointer-events: none;
  display: inline-block;
}

/* Hover popup: hidden by default, JS controls visibility */
/* !important needed to override EC's all:revert reset */
.glosharp-hover > .glosharp-popup-container {
  display: none !important;
  left: 0;
  top: calc(100% + 8px);
}

/* Static query container (persistent ^? results between lines) */
.glosharp-noline {
  position: relative;
  display: block;
}

.glosharp-static {
  display: block !important;
  position: relative;
  padding-left: var(--ec-codePadInl, 1.35rem);
}

.glosharp-static-container {
  display: block !important;
  z-index: 10;
  border: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
  border-radius: 4px;
  background: var(--glosharp-popup-bg, ${styleSettings.popupBackground.dark});
  color: var(--glosharp-popup-fg, ${styleSettings.popupForeground.dark});
  font-size: 90%;
  white-space: nowrap !important;
  word-break: normal !important;
  overflow-wrap: normal !important;
  width: max-content !important;
}

/* No arrow caret on static container — persistent query results render inline */
.glosharp-static-container::before {
  display: none !important;
}

.glosharp-popup-code {
  display: block;
  width: 100%;
  max-width: 600px !important;
  padding: 6px 12px;
  font-family: inherit;
  white-space: pre-wrap;
  max-height: 200px;
  overflow: auto;
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

.glosharp-popup-docs {
  max-width: 600px;
  padding: 6px 12px;
  border-top: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
  max-height: 200px;
  overflow: auto;
  text-wrap: balance;
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
  border-top: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
}

.glosharp-popup-section-label {
  font-size: 0.8em;
  opacity: 0.7;
  text-transform: uppercase;
  letter-spacing: 0.05em;
  margin-bottom: 2px;
}

.glosharp-popup-param {
  display: flex;
  gap: 6px;
  margin: 1px 0;
}

.glosharp-popup-param-name {
  font-family: inherit;
  font-weight: bold;
  white-space: nowrap;
}

.glosharp-popup-exception {
  display: flex;
  gap: 6px;
  margin: 1px 0;
}

.glosharp-popup-exception-type {
  font-family: inherit;
  font-weight: bold;
  white-space: nowrap;
}

.glosharp-popup-example pre {
  margin: 2px 0;
  padding: 4px 6px;
  background: rgba(128, 128, 128, 0.1);
  border-radius: 2px;
  font-size: 0.9em;
}

/* Static query: always visible, rendered below the line */
.glosharp-static {
  position: relative;
  display: block;
}

.glosharp-static > .glosharp-popup-container {
  position: relative;
  display: block;
  margin-top: 4px;
}

.glosharp-static > .glosharp-popup-container::before {
  top: -5px;
  left: 6px;
}

/* Error underlines */
.glosharp-error-underline {
  border-bottom: 2px wavy var(--glosharp-error-underline, ${styleSettings.errorUnderline.dark});
}

.glosharp-error-underline.glosharp-severity-warning {
  border-bottom-color: var(--glosharp-warning-underline, ${styleSettings.warningUnderline.dark});
}

.glosharp-error-underline.glosharp-severity-info {
  border-bottom-color: var(--glosharp-info-underline, ${styleSettings.infoUnderline.dark});
}

.glosharp-error-message {
  display: block;
  padding: 2px 8px;
  margin-top: 2px;
  background: var(--glosharp-error-bg, ${styleSettings.errorBackground.dark});
  border-left: 3px solid var(--glosharp-error-underline, ${styleSettings.errorUnderline.dark});
  color: var(--glosharp-error-underline, ${styleSettings.errorUnderline.dark});
  font-size: 0.85em;
}

.glosharp-error-message.glosharp-severity-warning {
  background: var(--glosharp-warning-bg, ${styleSettings.warningBackground.dark});
  border-left-color: var(--glosharp-warning-underline, ${styleSettings.warningUnderline.dark});
  color: var(--glosharp-warning-underline, ${styleSettings.warningUnderline.dark});
}

.glosharp-error-message.glosharp-severity-info {
  background: var(--glosharp-info-bg, ${styleSettings.infoBackground.dark});
  border-left-color: var(--glosharp-info-underline, ${styleSettings.infoUnderline.dark});
  color: var(--glosharp-info-underline, ${styleSettings.infoUnderline.dark});
}

.glosharp-error-code {
  font-weight: bold;
}

a.glosharp-error-code {
  color: inherit;
  text-decoration: none;
}

a.glosharp-error-code:hover {
  text-decoration: underline;
}

/* Completion list */
.glosharp-completion-list {
  list-style: none;
  margin: 4px 0 0 0;
  padding: 4px 0;
  border: 1px solid var(--glosharp-popup-border, ${styleSettings.popupBorder.dark});
  border-radius: 4px;
  background: var(--glosharp-popup-bg, ${styleSettings.popupBackground.dark});
  font-size: 0.875em;
  max-height: 200px;
  overflow-y: auto;
}

.glosharp-completion-item {
  display: flex;
  gap: 8px;
  padding: 2px 8px;
  align-items: center;
}

.glosharp-completion-kind {
  font-size: 0.75em;
  opacity: 0.7;
  min-width: 60px;
}

.glosharp-completion-label {
  color: var(--glosharp-popup-fg, ${styleSettings.popupForeground.dark});
}

.glosharp-completion-detail {
  opacity: 0.6;
  font-size: 0.85em;
  margin-left: auto;
}

/* Visual annotations */
.glosharp-highlight {
  background: var(--glosharp-highlight-bg, ${styleSettings.highlightBackground.dark});
}

.glosharp-focus-dim {
  opacity: var(--glosharp-focus-dim-opacity, ${styleSettings.focusDimOpacity.dark});
  transition: opacity 0.2s;
}

.glosharp-diff-add {
  background: var(--glosharp-diff-add-bg, ${styleSettings.diffAddBackground.dark});
  border-left: 3px solid var(--glosharp-diff-add-border, ${styleSettings.diffAddBorder.dark});
}

.glosharp-diff-remove {
  background: var(--glosharp-diff-remove-bg, ${styleSettings.diffRemoveBackground.dark});
  border-left: 3px solid var(--glosharp-diff-remove-border, ${styleSettings.diffRemoveBorder.dark});
}

${partColorRules}
`
}

// VS Code Codicon symbol icons (MIT licensed)
// Each entry: [svgPaths[], color, fillRule]
const symbolIcons: Record<string, { paths: string[], color: string, fillRule?: string }> = {
  Local: {
    paths: [
      'M11.279 5.79L8.799 5.066C8.59 5.006 8.372 5.015 8.168 5.09L4.648 6.41C4.26 6.556 4 6.932 4 7.347V9.14C4 9.571 4.274 9.952 4.684 10.088L7.165 10.915C7.268 10.95 7.376 10.967 7.483 10.967C7.611 10.967 7.739 10.943 7.859 10.894L11.376 9.465C11.755 9.312 12 8.948 12 8.539V6.75C12 6.308 11.703 5.913 11.279 5.79ZM11 8.539L7.483 9.968L5 9.14V7.347L8.521 6.027L11 6.751V8.54ZM7.48 7.467L8.807 6.914C9.06 6.809 9.355 6.928 9.461 7.183C9.566 7.438 9.446 7.731 9.191 7.837L7.999 8.334V8.626C7.999 8.902 7.775 9.126 7.499 9.126C7.223 9.126 6.999 8.902 6.999 8.626V8.361L6.591 8.225C6.329 8.138 6.188 7.855 6.275 7.593C6.364 7.331 6.647 7.192 6.908 7.277L7.48 7.467Z',
      'M12.5 14H11.5C11.224 14 11 13.776 11 13.5C11 13.224 11.224 13 11.5 13H12.5C12.776 13 13 12.775 13 12.5V3.5C13 3.225 12.776 3 12.5 3H11.5C11.224 3 11 2.776 11 2.5C11 2.224 11.224 2 11.5 2H12.5C13.327 2 14 2.673 14 3.5V12.5C14 13.327 13.327 14 12.5 14ZM5 13.5C5 13.224 4.776 13 4.5 13H3.5C3.224 13 3 12.775 3 12.5V3.5C3 3.225 3.224 3 3.5 3H4.5C4.776 3 5 2.776 5 2.5C5 2.224 4.776 2 4.5 2H3.5C2.673 2 2 2.673 2 3.5V12.5C2 13.327 2.673 14 3.5 14H4.5C4.776 14 5 13.776 5 13.5Z',
    ],
    color: '#75beff',
  },
  Parameter: {
    paths: ['M4.5 3C4.22386 3 4 3.22386 4 3.5V4.5C4 4.77614 4.22386 5 4.5 5C4.77614 5 5 4.77614 5 4.5V4H7.5V12H7C6.72386 12 6.5 12.2239 6.5 12.5C6.5 12.7761 6.72386 13 7 13H9C9.27614 13 9.5 12.7761 9.5 12.5C9.5 12.2239 9.27614 12 9 12H8.5V4H11V4.5C11 4.77614 11.2239 5 11.5 5C11.7761 5 12 4.77614 12 4.5V3.5C12 3.22386 11.7761 3 11.5 3H4.5ZM4.35355 6.64645C4.54882 6.84171 4.54882 7.15829 4.35355 7.35355L2.20711 9.5L4.35355 11.6464C4.54882 11.8417 4.54882 12.1583 4.35355 12.3536C4.15829 12.5488 3.84171 12.5488 3.64645 12.3536L1.14645 9.85355C0.951184 9.65829 0.951184 9.34171 1.14645 9.14645L3.64645 6.64645C3.84171 6.45118 4.15829 6.45118 4.35355 6.64645ZM14.8536 9.14645L12.3536 6.64645C12.1583 6.45118 11.8417 6.45118 11.6464 6.64645C11.4512 6.84171 11.4512 7.15829 11.6464 7.35355L13.7929 9.5L11.6464 11.6464C11.4512 11.8417 11.4512 12.1583 11.6464 12.3536C11.8417 12.5488 12.1583 12.5488 12.3536 12.3536L14.8536 9.85355C15.0488 9.65829 15.0488 9.34171 14.8536 9.14645Z'],
    color: '#75beff',
  },
  Field: {
    paths: ['M11.967 6.089C11.991 6.15 12.002 6.216 12.001 6.281C11.999 6.347 11.984 6.412 11.958 6.472C11.931 6.532 11.893 6.586 11.845 6.632C11.798 6.677 11.741 6.712 11.68 6.736L7.5 8.344V10.02C7.5 10.153 7.447 10.28 7.354 10.374C7.26 10.467 7.133 10.52 7 10.52C6.867 10.52 6.74 10.467 6.646 10.374C6.553 10.28 6.5 10.153 6.5 10.02V8.343L4.32 7.505C4.259 7.481 4.203 7.446 4.155 7.401C4.108 7.355 4.069 7.301 4.043 7.241C4.016 7.181 4.002 7.116 4 7.051C3.999 6.985 4.01 6.92 4.034 6.858C4.057 6.797 4.093 6.741 4.138 6.694C4.183 6.646 4.237 6.608 4.297 6.581C4.357 6.555 4.422 6.54 4.488 6.539C4.553 6.537 4.619 6.548 4.68 6.572L7 7.464L11.32 5.8C11.381 5.776 11.447 5.765 11.513 5.767C11.579 5.769 11.643 5.783 11.704 5.81C11.764 5.837 11.818 5.875 11.863 5.923C11.908 5.971 11.944 6.027 11.967 6.089ZM15 5.8V9.429C14.999 9.732 14.906 10.027 14.735 10.277C14.563 10.527 14.32 10.719 14.038 10.829L7.538 13.329C7.191 13.463 6.807 13.463 6.46 13.329L1.961 11.6C1.679 11.49 1.436 11.298 1.265 11.048C1.094 10.798 1.001 10.503 1 10.2V6.571C1.002 6.268 1.094 5.973 1.266 5.723C1.437 5.473 1.68 5.281 1.962 5.171L8.462 2.671C8.809 2.538 9.193 2.538 9.54 2.671L14.04 4.402C14.322 4.512 14.564 4.704 14.735 4.954C14.906 5.203 14.998 5.498 15 5.8ZM14 5.8C14 5.699 13.969 5.6 13.912 5.517C13.855 5.433 13.774 5.369 13.679 5.333L9.179 3.603C9.064 3.558 8.936 3.558 8.821 3.603L2.321 6.103C2.226 6.139 2.145 6.203 2.088 6.287C2.03 6.371 2 6.47 2 6.571V10.2C2 10.301 2.031 10.399 2.088 10.483C2.145 10.566 2.226 10.63 2.32 10.666L6.82 12.398C6.935 12.442 7.063 12.442 7.178 12.398L13.678 9.898C13.773 9.862 13.855 9.798 13.912 9.714C13.97 9.63 14 9.531 14 9.429V5.8Z'],
    color: '#75beff',
  },
  Property: {
    paths: ['M7 5C7 2.791 8.791 1 11 1C11.509 1 11.996 1.095 12.445 1.269C12.603 1.33 12.719 1.467 12.754 1.632C12.789 1.798 12.738 1.97 12.618 2.089L10.707 4L12 5.293L13.911 3.382C14.031 3.263 14.202 3.212 14.368 3.246C14.533 3.281 14.67 3.397 14.731 3.555C14.905 4.004 15 4.491 15 5C15 7.209 13.209 9 11 9C10.62 9 10.251 8.947 9.902 8.847L4.896 13.919C4.182 14.643 3.037 14.71 2.243 14.076C1.329 13.346 1.241 11.987 2.053 11.145L7.104 5.911C7.036 5.618 7 5.313 7 5ZM11 2C9.343 2 8 3.343 8 5C8 5.315 8.048 5.619 8.138 5.903C8.193 6.078 8.148 6.269 8.021 6.401L2.773 11.84C2.37 12.258 2.413 12.932 2.867 13.294C3.261 13.609 3.83 13.576 4.184 13.217L9.408 7.924C9.547 7.784 9.756 7.738 9.941 7.808C10.269 7.932 10.626 8 11 8C12.657 8 14 6.657 14 5C14 4.906 13.996 4.812 13.987 4.72L12.354 6.354C12.26 6.447 12.133 6.5 12 6.5C11.868 6.5 11.74 6.447 11.647 6.354L9.647 4.354C9.451 4.158 9.451 3.842 9.647 3.647L11.28 2.013C11.188 2.004 11.095 2 11 2Z'],
    color: '#75beff',
  },
  Method: {
    paths: ['M4.697 5.04C4.443 4.932 4.149 5.049 4.04 5.303C3.932 5.557 4.049 5.851 4.303 5.96L7.5 7.33V10.5C7.5 10.776 7.724 11 8 11C8.276 11 8.5 10.776 8.5 10.5V7.33L11.697 5.96C11.951 5.851 12.068 5.557 11.96 5.303C11.851 5.049 11.557 4.932 11.303 5.04L8 6.456L4.697 5.04ZM9.077 1.379C8.384 1.112 7.616 1.112 6.923 1.379L1.962 3.287C1.382 3.51 1 4.066 1 4.687V11.313C1 11.934 1.382 12.491 1.962 12.713L6.923 14.622C7.616 14.888 8.384 14.888 9.077 14.622L14.039 12.713C14.618 12.491 15 11.934 15 11.313V4.687C15 4.066 14.618 3.51 14.039 3.287L9.077 1.379ZM7.282 2.312C7.744 2.134 8.256 2.134 8.718 2.312L13.68 4.22C13.873 4.294 14 4.48 14 4.687V11.313C14 11.52 13.873 11.706 13.68 11.78L8.718 13.688C8.256 13.866 7.744 13.866 7.282 13.688L2.321 11.78C2.127 11.706 2 11.52 2 11.313V4.687C2 4.48 2.127 4.294 2.321 4.22L7.282 2.312Z'],
    color: '#b180d7',
  },
  Class: {
    paths: ['M13.207 10.5C13.019 10.313 12.765 10.207 12.5 10.207C12.235 10.207 11.981 10.313 11.793 10.5L11.293 11H9V7H10.363C10.248 7.188 10.199 7.408 10.224 7.627C10.249 7.846 10.346 8.05 10.5 8.207L11.293 9C11.481 9.187 11.735 9.293 12 9.293C12.265 9.293 12.519 9.187 12.707 9L14 7.707C14.187 7.519 14.293 7.265 14.293 7C14.293 6.735 14.187 6.48 14 6.293L13.207 5.5C13.019 5.312 12.765 5.207 12.5 5.207C12.235 5.207 11.981 5.312 11.793 5.5L11.293 6H6.707L7.5 5.207C7.687 5.019 7.793 4.765 7.793 4.5C7.793 4.235 7.687 3.98 7.5 3.793L6.207 2.5C6.019 2.312 5.765 2.207 5.5 2.207C5.235 2.207 4.98 2.312 4.793 2.5L1.5 5.793C1.312 5.98 1.207 6.235 1.207 6.5C1.207 6.765 1.312 7.019 1.5 7.207L2.793 8.5C2.98 8.687 3.235 8.793 3.5 8.793C3.765 8.793 4.019 8.687 4.207 8.5L5.707 7H8V11.5C8 11.633 8.053 11.76 8.146 11.854C8.24 11.947 8.367 12 8.5 12H10.363C10.248 12.188 10.199 12.408 10.224 12.627C10.249 12.846 10.346 13.05 10.5 13.207L11.293 14C11.481 14.187 11.735 14.293 12 14.293C12.265 14.293 12.519 14.187 12.707 14L14 12.707C14.187 12.519 14.293 12.265 14.293 12C14.293 11.735 14.187 11.481 14 11.293L13.207 10.5ZM3.5 7.793L2.207 6.5L5.5 3.207L6.793 4.5L3.5 7.793ZM13.293 7L12 8.293L11.207 7.5L12.5 6.207L13.293 7ZM12 13.293L11.207 12.5L12.5 11.207L13.293 12L12 13.293Z'],
    color: '#ee9d28',
  },
  Struct: {
    paths: [
      'M1 3C1 2.448 1.448 2 2 2H14C14.552 2 15 2.448 15 3V6C15 6.552 14.552 7 14 7H2C1.448 7 1 6.552 1 6V3ZM2 3H14V6H2V3Z',
      'M2 9C1.448 9 1 9.448 1 10V13C1 13.552 1.448 14 2 14H5C5.552 14 6 13.552 6 13V10C6 9.448 5.552 9 5 9H2ZM5 10H2V13H5V10Z',
      'M11 9C10.448 9 10 9.448 10 10V13C10 13.552 10.448 14 11 14H14C14.552 14 15 13.552 15 13V10C15 9.448 14.552 9 14 9H11ZM14 10H11V13H14V10Z',
    ],
    color: '#ee9d28',
  },
  Interface: {
    paths: ['M11.5 4.5C9.742 4.5 8.296 5.808 8.051 7.5H4.929C4.705 6.64 3.929 6 3 6C1.897 6 1 6.897 1 8C1 9.103 1.897 10 3 10C3.929 10 4.705 9.36 4.929 8.5H8.051C8.296 10.192 9.742 11.5 11.5 11.5C13.43 11.5 15 9.93 15 8C15 6.07 13.43 4.5 11.5 4.5ZM3 9C2.448 9 2 8.551 2 8C2 7.449 2.448 7 3 7C3.552 7 4 7.449 4 8C4 8.551 3.552 9 3 9ZM11.5 10.5C10.121 10.5 9 9.378 9 8C9 6.622 10.121 5.5 11.5 5.5C12.879 5.5 14 6.622 14 8C14 9.378 12.879 10.5 11.5 10.5Z'],
    color: '#5bb6a9',
  },
  Enum: {
    paths: ['M15 3.5V7.5C15 7.9 14.85 8.28 14.56 8.56C14.28 8.84 13.9 9 13.5 9H11V8.5C11 8.33 10.98 8.16 10.95 8H13.5C13.63 8 13.76 7.95 13.85 7.85C13.95 7.76 14 7.63 14 7.5V3.5C14 3.37 13.95 3.24 13.85 3.15C13.76 3.05 13.63 3 13.5 3H7.5C7.37 3 7.24 3.05 7.15 3.15C7.05 3.24 7 3.37 7 3.5V6H6V3.5C6 3.1 6.16 2.72 6.44 2.44C6.72 2.16 7.1 2 7.5 2H13.5C13.9 2 14.28 2.16 14.56 2.44C14.84 2.72 15 3.1 15 3.5ZM12.5 5C12.776 5 13 4.776 13 4.5C13 4.224 12.776 4 12.5 4H8.5C8.224 4 8 4.224 8 4.5C8 4.776 8.224 5 8.5 5H12.5ZM13 6.5C13 6.22 12.78 6 12.5 6H8.5C9.32 6 10.04 6.39 10.5 7H12.5C12.78 7 13 6.78 13 6.5ZM7.5 9H3.5C3.224 9 3 9.224 3 9.5C3 9.776 3.224 10 3.5 10H7.5C7.776 10 8 9.776 8 9.5C8 9.224 7.776 9 7.5 9ZM7.5 11H3.5C3.224 11 3 11.224 3 11.5C3 11.776 3.224 12 3.5 12H7.5C7.776 12 8 11.776 8 11.5C8 11.224 7.776 11 7.5 11ZM10 8.5V12.5C10 13.327 9.327 14 8.5 14H2.5C1.673 14 1 13.327 1 12.5V8.5C1 7.673 1.673 7 2.5 7H8.5C9.327 7 10 7.673 10 8.5ZM9 8.5C9 8.225 8.775 8 8.5 8H2.5C2.225 8 2 8.225 2 8.5V12.5C2 12.775 2.225 13 2.5 13H8.5C8.775 13 9 12.775 9 12.5V8.5Z'],
    color: '#ee9d28',
  },
  Delegate: {
    paths: ['M4.697 5.04C4.443 4.932 4.149 5.049 4.04 5.303C3.932 5.557 4.049 5.851 4.303 5.96L7.5 7.33V10.5C7.5 10.776 7.724 11 8 11C8.276 11 8.5 10.776 8.5 10.5V7.33L11.697 5.96C11.951 5.851 12.068 5.557 11.96 5.303C11.851 5.049 11.557 4.932 11.303 5.04L8 6.456L4.697 5.04ZM9.077 1.379C8.384 1.112 7.616 1.112 6.923 1.379L1.962 3.287C1.382 3.51 1 4.066 1 4.687V11.313C1 11.934 1.382 12.491 1.962 12.713L6.923 14.622C7.616 14.888 8.384 14.888 9.077 14.622L14.039 12.713C14.618 12.491 15 11.934 15 11.313V4.687C15 4.066 14.618 3.51 14.039 3.287L9.077 1.379ZM7.282 2.312C7.744 2.134 8.256 2.134 8.718 2.312L13.68 4.22C13.873 4.294 14 4.48 14 4.687V11.313C14 11.52 13.873 11.706 13.68 11.78L8.718 13.688C8.256 13.866 7.744 13.866 7.282 13.688L2.321 11.78C2.127 11.706 2 11.52 2 11.313V4.687C2 4.48 2.127 4.294 2.321 4.22L7.282 2.312Z'],
    color: '#b180d7',
  },
  Namespace: {
    paths: ['M5 2C3.895 2 3 2.895 3 4V6.005C3 6.535 2.992 6.799 2.911 7.002C2.848 7.158 2.716 7.333 2.276 7.553C2.107 7.637 2 7.811 2 8C2 8.189 2.107 8.362 2.276 8.447C2.716 8.667 2.848 8.842 2.911 8.998C2.992 9.202 3 9.467 3 10V12C3 13.105 3.895 14 5 14C5.276 14 5.5 13.776 5.5 13.5C5.5 13.224 5.276 13 5 13C4.448 13 4 12.552 4 12V9.941C4 9.492 4.001 9.03 3.839 8.627C3.742 8.384 3.597 8.18 3.401 8C3.596 7.821 3.742 7.616 3.839 7.374C4.001 6.971 4 6.51 4 6.063V4C4 3.448 4.448 3 5 3C5.276 3 5.5 2.776 5.5 2.5C5.5 2.224 5.276 2 5 2ZM11 2C12.105 2 13 2.895 13 4V6.005C13 6.535 13.008 6.799 13.089 7.002C13.152 7.158 13.284 7.333 13.724 7.553C13.893 7.637 14 7.811 14 8C14 8.189 13.893 8.362 13.724 8.447C13.284 8.667 13.152 8.842 13.089 8.998C13.008 9.202 13 9.467 13 10V12C13 13.105 12.105 14 11 14C10.724 14 10.5 13.776 10.5 13.5C10.5 13.224 10.724 13 11 13C11.552 13 12 12.552 12 12V9.941C12 9.492 11.999 9.03 12.161 8.627C12.258 8.384 12.404 8.18 12.599 8C12.404 7.821 12.258 7.616 12.161 7.374C11.999 6.971 12 6.51 12 6.063V4C12 3.448 11.552 3 11 3C10.724 3 10.5 2.776 10.5 2.5C10.5 2.224 10.724 2 11 2Z'],
    color: '#aaa',
  },
  Event: {
    paths: ['M4.914 1.714C5.041 1.291 5.431 1 5.873 1H10.279C10.977 1 11.46 1.697 11.215 2.351L10.222 5H12.251C12.879 5 13.229 5.726 12.838 6.217L6.231 14.517C5.377 15.59 3.669 14.705 4.053 13.389L5.333 9H3.75C3.248 9 2.887 8.517 3.031 8.036L4.914 1.714ZM10.279 2H5.873L4.085 8H6C6.157 8 6.306 8.074 6.4 8.2C6.495 8.326 6.524 8.489 6.48 8.64L5.013 13.669C4.989 13.75 5.001 13.805 5.02 13.843C5.041 13.888 5.081 13.931 5.138 13.961C5.195 13.99 5.254 13.998 5.302 13.99C5.345 13.983 5.396 13.96 5.449 13.895L11.733 6H9.5C9.336 6 9.183 5.92 9.089 5.785C8.996 5.65 8.974 5.478 9.032 5.324L10.279 2Z'],
    color: '#ee9d28',
  },
  Type: {
    paths: ['M13.207 10.5C13.019 10.313 12.765 10.207 12.5 10.207C12.235 10.207 11.981 10.313 11.793 10.5L11.293 11H9V7H10.363C10.248 7.188 10.199 7.408 10.224 7.627C10.249 7.846 10.346 8.05 10.5 8.207L11.293 9C11.481 9.187 11.735 9.293 12 9.293C12.265 9.293 12.519 9.187 12.707 9L14 7.707C14.187 7.519 14.293 7.265 14.293 7C14.293 6.735 14.187 6.48 14 6.293L13.207 5.5C13.019 5.312 12.765 5.207 12.5 5.207C12.235 5.207 11.981 5.312 11.793 5.5L11.293 6H6.707L7.5 5.207C7.687 5.019 7.793 4.765 7.793 4.5C7.793 4.235 7.687 3.98 7.5 3.793L6.207 2.5C6.019 2.312 5.765 2.207 5.5 2.207C5.235 2.207 4.98 2.312 4.793 2.5L1.5 5.793C1.312 5.98 1.207 6.235 1.207 6.5C1.207 6.765 1.312 7.019 1.5 7.207L2.793 8.5C2.98 8.687 3.235 8.793 3.5 8.793C3.765 8.793 4.019 8.687 4.207 8.5L5.707 7H8V11.5C8 11.633 8.053 11.76 8.146 11.854C8.24 11.947 8.367 12 8.5 12H10.363C10.248 12.188 10.199 12.408 10.224 12.627C10.249 12.846 10.346 13.05 10.5 13.207L11.293 14C11.481 14.187 11.735 14.293 12 14.293C12.265 14.293 12.519 14.187 12.707 14L14 12.707C14.187 12.519 14.293 12.265 14.293 12C14.293 11.735 14.187 11.481 14 11.293L13.207 10.5ZM3.5 7.793L2.207 6.5L5.5 3.207L6.793 4.5L3.5 7.793ZM13.293 7L12 8.293L11.207 7.5L12.5 6.207L13.293 7ZM12 13.293L11.207 12.5L12.5 11.207L13.293 12L12 13.293Z'],
    color: '#ee9d28',
  },
}

// Build SVG sprite sheet HTML from symbol icon data
function buildSpriteSheetHtml(): string {
  const symbols = Object.entries(symbolIcons).map(([kind, icon]) => {
    const paths = icon.paths.map(d => `<path d="${d}" fill="${icon.color}"/>`).join('')
    return `<symbol id="glosharp-icon-${kind}" viewBox="0 0 16 16">${paths}</symbol>`
  }).join('')
  return `<svg xmlns="http://www.w3.org/2000/svg" style="display:none">${symbols}</svg>`
}

// Client-side JS for interactive hover popups
function buildPopupJsModule(): string {
  const spriteHtml = buildSpriteSheetHtml().replace(/\\/g, '\\\\').replace(/'/g, "\\'").replace(/`/g, '\\`')
  return `
(function initGloSharpPopups() {
  const TIMEOUT_MS = 100;
  let activePopup = null;
  let activeHover = null;
  let hideTimeout = null;

  function showTooltip(hoverEl) {
    const popup = hoverEl.querySelector('.glosharp-popup-container');
    if (!popup) return;

    // Find the .expressive-code ancestor to reparent into
    const ecRoot = hoverEl.closest('.expressive-code');
    if (!ecRoot) return;

    // Cancel any pending hide
    if (hideTimeout) { clearTimeout(hideTimeout); hideTimeout = null; }

    // Hide previous popup
    if (activePopup && activePopup !== popup) {
      hideTooltip();
    }

    // Reparent popup to EC root for overflow
    ecRoot.appendChild(popup);
    popup.style.setProperty('display', 'block', 'important');
    // Re-trigger fade-in animation
    popup.style.animation = 'none';
    popup.offsetHeight; // force reflow
    popup.style.animation = '';

    // Position below the token
    const hoverRect = hoverEl.getBoundingClientRect();
    const ecRect = ecRoot.getBoundingClientRect();
    const left = hoverRect.left - ecRect.left;
    const top = hoverRect.bottom - ecRect.top + 6;

    popup.style.left = left + 'px';
    popup.style.top = top + 'px';

    // Adjust arrow position
    const arrow = popup.querySelector(':scope')
    // Arrow is via ::before, positioned at left:12px by default — that's fine

    activePopup = popup;
    activeHover = hoverEl;
    popup._glosharpOrigParent = hoverEl;
  }

  function hideTooltip() {
    if (hideTimeout) { clearTimeout(hideTimeout); hideTimeout = null; }
    if (activePopup) {
      activePopup.style.setProperty('display', 'none', 'important');
      // Reparent back
      if (activePopup._glosharpOrigParent) {
        activePopup._glosharpOrigParent.appendChild(activePopup);
      }
      activePopup = null;
      activeHover = null;
    }
  }

  function scheduleHide() {
    if (hideTimeout) clearTimeout(hideTimeout);
    hideTimeout = setTimeout(() => {
      hideTooltip();
    }, TIMEOUT_MS);
  }

  function injectSpriteSheet() {
    if (document.getElementById('glosharp-sprites')) return;
    const div = document.createElement('div');
    div.id = 'glosharp-sprites';
    div.innerHTML = '${spriteHtml}';
    document.body.prepend(div);
  }

  function setup() {
    injectSpriteSheet();
    document.querySelectorAll('.glosharp-hover').forEach(hoverEl => {
      if (hoverEl._glosharpBound) return;
      hoverEl._glosharpBound = true;

      hoverEl.addEventListener('mouseenter', () => showTooltip(hoverEl));
      hoverEl.addEventListener('mouseleave', () => scheduleHide());
    });

    // Allow mouse to enter popups without them disappearing
    document.addEventListener('mouseenter', (e) => {
      if (e.target && e.target.closest && e.target.closest('.glosharp-popup-container') === activePopup) {
        if (hideTimeout) { clearTimeout(hideTimeout); hideTimeout = null; }
      }
    }, true);

    document.addEventListener('mouseleave', (e) => {
      if (e.target && e.target.closest && e.target.closest('.glosharp-popup-container') === activePopup) {
        scheduleHide();
      }
    }, true);
  }

  // Run on load
  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', setup);
  } else {
    setup();
  }

  // Support Astro view transitions
  document.addEventListener('astro:page-load', setup);

  // Watch for dynamically added code blocks
  new MutationObserver((mutations) => {
    for (const m of mutations) {
      for (const node of m.addedNodes) {
        if (node.nodeType === 1 && node.querySelector && node.querySelector('.glosharp-hover')) {
          setup();
          return;
        }
      }
    }
  }).observe(document.body, { childList: true, subtree: true });
})();
`
}

export function pluginGloSharp(options: PluginGloSharpOptions = {}) {
  const glosharp = createGloSharp(options)
  const resultCache = new WeakMap<ExpressiveCodeBlock, GloSharpResult>()

  return {
    name: 'glosharp',
    baseStyles: buildBaseStyles(),
    jsModules: [buildPopupJsModule()],

    hooks: {
      async preprocessCode({ codeBlock }: { codeBlock: ExpressiveCodeBlock }) {
        const lang = codeBlock.language
        if (lang !== 'csharp' && lang !== 'cs' && lang !== 'c#') return

        try {
          const result = await glosharp.process({ code: codeBlock.code, project: options.project, region: options.region })
          resultCache.set(codeBlock, result)
          // Replace code with cleaned version (markers removed)
          const cleaned = result.code.replace(/\n$/, '')
          const newLines = cleaned === '' ? [] : cleaned.split('\n')
          const oldLines = codeBlock.getLines()
          const indicesToDelete = Array.from({ length: oldLines.length }, (_, i) => i).reverse()
          if (indicesToDelete.length > 0) {
            codeBlock.deleteLines(indicesToDelete)
          }
          if (newLines.length > 0) {
            codeBlock.insertLines(0, newLines)
          }
        } catch {
          // Silently pass through if CLI fails
        }
      },

      annotateCode({ codeBlock }: { codeBlock: { code: string; getLines: () => Array<{ addAnnotation: (ann: unknown) => void }> } }) {
        const result = resultCache.get(codeBlock as unknown as ExpressiveCodeBlock)
        if (!result) return

        const lines = codeBlock.getLines()

        // Add hover annotations
        for (const hover of result.hovers) {
          const line = lines[hover.line]
          if (!line) continue
          if (hover.persistent) {
            // Persistent ^? query: render as static inline block
            line.addAnnotation(new GloSharpStaticAnnotation(hover))
          } else {
            // Regular hover: JS-powered popup
            line.addAnnotation(new GloSharpHoverAnnotation(hover))
          }
        }

        // Add error annotations
        for (const error of result.errors) {
          if (error.expected) continue

          if (error.endLine != null && error.endLine > error.line) {
            for (let lineIdx = error.line; lineIdx <= error.endLine; lineIdx++) {
              const line = lines[lineIdx]
              if (!line) continue
              const lineError = { ...error }
              if (lineIdx === error.line) {
                line.addAnnotation(new GloSharpErrorAnnotation(lineError))
              } else {
                const contError = { ...error, character: 0, length: 1000 }
                line.addAnnotation(new GloSharpErrorAnnotation(contError))
              }
            }
            const lastLine = lines[error.endLine]
            if (lastLine) {
              lastLine.addAnnotation(new GloSharpErrorAnnotation(error, { messageOnly: true }))
            }
          } else {
            const line = lines[error.line]
            if (!line) continue
            line.addAnnotation(new GloSharpErrorAnnotation(error))
          }
        }

        // Add completion annotations
        for (const completion of result.completions) {
          const line = lines[completion.line]
          if (!line) continue
          line.addAnnotation(new GloSharpCompletionAnnotation(completion))
        }

        // Add highlight and diff annotations
        const hasFocus = result.highlights.some(h => h.kind === 'focus')
        const focusedLines = new Set(result.highlights.filter(h => h.kind === 'focus').map(h => h.line))

        for (const highlight of result.highlights) {
          const line = lines[highlight.line]
          if (!line) continue

          switch (highlight.kind) {
            case 'highlight':
              line.addAnnotation(new GloSharpHighlightAnnotation())
              break
            case 'add':
            case 'remove':
              line.addAnnotation(new GloSharpDiffAnnotation(highlight.kind))
              break
          }
        }

        if (hasFocus) {
          for (let i = 0; i < lines.length; i++) {
            if (!focusedLines.has(i)) {
              lines[i].addAnnotation(new GloSharpFocusDimAnnotation())
            }
          }
        }
      },

      postprocessRenderedBlock({ renderData }: { renderData: { blockAst: unknown } }) {
        // Annotations handle rendering via their render() methods
      },
    },
  }
}

function buildSymbolIcon(kind: string, prefix: string): HastNode {
  const icon = symbolIcons[kind] ?? symbolIcons['Type']!
  return {
    type: 'element',
    tagName: 'span',
    properties: {
      class: 'glosharp-symbol-icon',
      title: prefix,
    },
    children: [{
      type: 'element',
      tagName: 'svg',
      properties: {
        xmlns: 'http://www.w3.org/2000/svg',
        viewBox: '0 0 16 16',
        width: '14',
        height: '14',
        fill: icon.color,
      },
      children: [{
        type: 'element',
        tagName: 'use',
        properties: { href: `#glosharp-icon-${kind in symbolIcons ? kind : 'Type'}` },
        children: [],
      }],
    }],
  }
}

// Build popup content nodes (shared by hover and static annotations)
function buildPopupContent(hover: GloSharpHover): HastNode[] {
  const parts = hover.parts
  let iconNode: HastNode | null = null
  let startIdx = 0

  // Detect prefix pattern: "(", text, ")", " " at start of parts
  if (parts.length >= 4
    && parts[0].text === '(' && parts[0].kind === 'punctuation'
    && parts[2].text === ')' && parts[2].kind === 'punctuation'
    && parts[3].text === ' ' && parts[3].kind === 'space'
    && parts[1].kind === 'text') {
    const prefix = parts[1].text
    iconNode = buildSymbolIcon(hover.symbolKind, prefix)
    startIdx = 4
  }

  const partNodes: HastNode[] = parts.slice(startIdx).map((part: GloSharpDisplayPart) => ({
    type: 'element' as const,
    tagName: 'span',
    properties: { class: `glosharp-${part.kind}` },
    children: [{ type: 'text' as const, value: part.text }],
  }))

  if (iconNode) {
    partNodes.unshift(iconNode)
  }

  const children: HastNode[] = [{
    type: 'element',
    tagName: 'code',
    properties: { class: 'glosharp-popup-code' },
    children: partNodes,
  }]

  if (hover.docs) {
    children.push(...renderDocs(hover.docs))
  }

  return children
}

function renderDocs(docs: GloSharpDocComment): HastNode[] {
  const docsChildren: HastNode[] = []

  if (docs.summary) {
    docsChildren.push({
      type: 'element',
      tagName: 'div',
      properties: { class: 'glosharp-popup-summary' },
      children: [{ type: 'text', value: docs.summary }],
    })
  }

  if (docs.params && docs.params.length > 0) {
    const paramItems: HastNode[] = docs.params.map((p: GloSharpDocParam) => ({
      type: 'element' as const,
      tagName: 'div',
      properties: { class: 'glosharp-popup-param' },
      children: [
        {
          type: 'element' as const,
          tagName: 'span',
          properties: { class: 'glosharp-popup-param-name' },
          children: [{ type: 'text' as const, value: p.name }],
        },
        { type: 'text' as const, value: ` — ${p.text}` },
      ],
    }))

    docsChildren.push({
      type: 'element',
      tagName: 'div',
      properties: { class: 'glosharp-popup-params' },
      children: [
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'glosharp-popup-section-label' },
          children: [{ type: 'text', value: 'Parameters' }],
        },
        ...paramItems,
      ],
    })
  }

  if (docs.returns) {
    docsChildren.push({
      type: 'element',
      tagName: 'div',
      properties: { class: 'glosharp-popup-returns' },
      children: [
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'glosharp-popup-section-label' },
          children: [{ type: 'text', value: 'Returns' }],
        },
        { type: 'text', value: docs.returns },
      ],
    })
  }

  if (docs.remarks) {
    docsChildren.push({
      type: 'element',
      tagName: 'div',
      properties: { class: 'glosharp-popup-remarks' },
      children: [
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'glosharp-popup-section-label' },
          children: [{ type: 'text', value: 'Remarks' }],
        },
        { type: 'text', value: docs.remarks },
      ],
    })
  }

  if (docs.examples && docs.examples.length > 0) {
    const exampleNodes: HastNode[] = docs.examples.map((ex: string) => ({
      type: 'element' as const,
      tagName: 'pre',
      properties: {},
      children: [{ type: 'text' as const, value: ex }],
    }))

    docsChildren.push({
      type: 'element',
      tagName: 'div',
      properties: { class: 'glosharp-popup-example' },
      children: [
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'glosharp-popup-section-label' },
          children: [{ type: 'text', value: 'Examples' }],
        },
        ...exampleNodes,
      ],
    })
  }

  if (docs.exceptions && docs.exceptions.length > 0) {
    const exceptionItems: HastNode[] = docs.exceptions.map((e: GloSharpDocException) => ({
      type: 'element' as const,
      tagName: 'div',
      properties: { class: 'glosharp-popup-exception' },
      children: [
        {
          type: 'element' as const,
          tagName: 'span',
          properties: { class: 'glosharp-popup-exception-type' },
          children: [{ type: 'text' as const, value: e.type }],
        },
        { type: 'text' as const, value: ` — ${e.text}` },
      ],
    }))

    docsChildren.push({
      type: 'element',
      tagName: 'div',
      properties: { class: 'glosharp-popup-exceptions' },
      children: [
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'glosharp-popup-section-label' },
          children: [{ type: 'text', value: 'Exceptions' }],
        },
        ...exceptionItems,
      ],
    })
  }

  if (docsChildren.length === 0) return []

  return [{
    type: 'element',
    tagName: 'div',
    properties: { class: 'glosharp-popup-docs' },
    children: docsChildren,
  }]
}

// Hover annotation: wraps token, popup shown/hidden by JS
class GloSharpHoverAnnotation {
  readonly hover: GloSharpHover
  readonly inlineRange: { columnStart: number; columnEnd: number }

  constructor(hover: GloSharpHover) {
    this.hover = hover
    this.inlineRange = {
      columnStart: hover.character,
      columnEnd: hover.character + hover.length,
    }
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    const popupChildren = buildPopupContent(this.hover)

    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'span',
      properties: { class: 'glosharp-hover' },
      children: [
        node,
        {
          type: 'element',
          tagName: 'div',
          properties: { class: 'glosharp-popup-container' },
          children: popupChildren,
        },
      ],
    }))
  }
}

// Static annotation: persistent ^? query, rendered as a block between lines
// This is a LINE-level annotation (no inlineRange) so it wraps the entire line
// and appends the static tooltip after the line content.
class GloSharpStaticAnnotation {
  readonly hover: GloSharpHover

  constructor(hover: GloSharpHover) {
    this.hover = hover
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    const popupChildren = buildPopupContent(this.hover)
    const marginLeft = this.hover.character

    // Wrap the line + append the static block after it
    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'div',
      properties: { class: 'glosharp-noline' },
      children: [
        node,
        {
          type: 'element' as const,
          tagName: 'div',
          properties: {
            class: 'glosharp-static',
            style: `margin-left: ${marginLeft}ch`,
          },
          children: [{
            type: 'element',
            tagName: 'div',
            properties: { class: 'glosharp-static-container' },
            children: popupChildren,
          }],
        },
      ],
    }))
  }
}

// Annotation classes (unchanged)
const CS_CODE_REGEX = /^CS\d+$/

function buildErrorCodeNode(code: string): HastNode {
  if (CS_CODE_REGEX.test(code)) {
    return {
      type: 'element',
      tagName: 'a',
      properties: {
        class: 'glosharp-error-code',
        href: `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/${code.toLowerCase()}`,
        target: '_blank',
        rel: 'noopener',
      },
      children: [{ type: 'text', value: code }],
    }
  }
  return {
    type: 'element',
    tagName: 'span',
    properties: { class: 'glosharp-error-code' },
    children: [{ type: 'text', value: code }],
  }
}

class GloSharpErrorAnnotation {
  readonly error: GloSharpError
  readonly inlineRange: { columnStart: number; columnEnd: number }
  readonly isMessageOnly: boolean

  constructor(error: GloSharpError, opts?: { messageOnly?: boolean }) {
    this.error = error
    this.isMessageOnly = opts?.messageOnly ?? false
    this.inlineRange = {
      columnStart: error.character,
      columnEnd: error.character + error.length,
    }
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    const severityClass = `glosharp-severity-${this.error.severity}`

    if (this.isMessageOnly) {
      return [
        ...nodesToTransform,
        {
          type: 'element',
          tagName: 'div',
          properties: { class: `glosharp-error-message ${severityClass}` },
          children: [
            buildErrorCodeNode(this.error.code),
            { type: 'text', value: `: ${this.error.message}` },
          ],
        },
      ]
    }

    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'span',
      properties: { class: `glosharp-error-underline ${severityClass}` },
      children: [
        node,
        {
          type: 'element',
          tagName: 'div',
          properties: { class: `glosharp-error-message ${severityClass}` },
          children: [
            buildErrorCodeNode(this.error.code),
            { type: 'text', value: `: ${this.error.message}` },
          ],
        },
      ],
    }))
  }
}

class GloSharpCompletionAnnotation {
  readonly completion: GloSharpCompletion
  readonly inlineRange: { columnStart: number; columnEnd: number }

  constructor(completion: GloSharpCompletion) {
    this.completion = completion
    this.inlineRange = {
      columnStart: completion.character,
      columnEnd: completion.character,
    }
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    const items: HastNode[] = this.completion.items.map(item => ({
      type: 'element' as const,
      tagName: 'li',
      properties: { class: `glosharp-completion-item glosharp-completion-kind-${item.kind}` },
      children: [
        {
          type: 'element',
          tagName: 'span',
          properties: { class: 'glosharp-completion-kind' },
          children: [{ type: 'text', value: item.kind }],
        },
        {
          type: 'element',
          tagName: 'span',
          properties: { class: 'glosharp-completion-label' },
          children: [{ type: 'text', value: item.label }],
        },
        ...(item.detail ? [{
          type: 'element' as const,
          tagName: 'span',
          properties: { class: 'glosharp-completion-detail' },
          children: [{ type: 'text', value: item.detail }],
        }] : []),
      ],
    }))

    const completionList: HastNode = {
      type: 'element',
      tagName: 'ul',
      properties: { class: 'glosharp-completion-list' },
      children: items,
    }

    return [...nodesToTransform, completionList]
  }
}

class GloSharpHighlightAnnotation {
  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'div',
      properties: { class: 'glosharp-highlight' },
      children: [node],
    }))
  }
}

class GloSharpDiffAnnotation {
  readonly diffKind: 'add' | 'remove'

  constructor(diffKind: 'add' | 'remove') {
    this.diffKind = diffKind
  }

  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'div',
      properties: { class: `glosharp-diff-${this.diffKind}` },
      children: [node],
    }))
  }
}

class GloSharpFocusDimAnnotation {
  render({ nodesToTransform }: { nodesToTransform: HastNode[] }): HastNode[] {
    return nodesToTransform.map(node => ({
      type: 'element' as const,
      tagName: 'div',
      properties: { class: 'glosharp-focus-dim' },
      children: [node],
    }))
  }
}

interface HastNode {
  type: string
  tagName?: string
  properties?: Record<string, unknown>
  children?: HastNode[]
  value?: string
}
