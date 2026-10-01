export type {
  GloSharpResult,
  GloSharpHover,
  GloSharpError,
  GloSharpDiagnosticCode,
  GloSharpDisplayPart,
  GloSharpMeta,
  GloSharpOptions,
  GloSharpExecutable,
  GloSharpProcessOptions,
  GloSharpRenderOptions,
  GloSharpCompletion,
  GloSharpCompletionItem,
  GloSharpCompletionKind,
  GloSharpDocComment,
  GloSharpDocParam,
  GloSharpDocException,
  GloSharpHighlight,
  GloSharpTag,
  GloSharpTypeAnnotation,
} from './types.js'
export { createGloSharp } from './glosharp.js'
export type { GloSharpInstance } from './glosharp.js'
export { GloSharpCliError, isGloSharpCliError, unexpectedErrors } from './errors.js'
export type { GloSharpCliErrorKind } from './errors.js'
export { configureGloSharp } from './limiter.js'
export type { GloSharpGlobalOptions } from './limiter.js'
export { resolveExecutable, clearExecutableCache, INSTALL_HINT } from './executable.js'
export type { ResolvedExecutable } from './executable.js'
export { EXPECTED_CLI_VERSION, checkCliVersion, resetCliVersionCheck } from './compat.js'
export { snippetKey, keyOptions, RESULT_AFFECTING_OPTIONS } from './key.js'
export type { GloSharpKeyOptions } from './key.js'
export { canonicalizeSnippet, hasGloSharpMarkers, GLOSHARP_MARKER_PATTERN } from './snippet.js'
