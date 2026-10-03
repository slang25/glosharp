export {
  transformerGloSharpWithResult,
  transformerGloSharpFromMap,
  processGloSharpCode,
  processGloSharpBlocks,
} from './transformer.js'
export type {
  TransformerGloSharpOptions,
  TransformerGloSharpFromMapOptions,
  GloSharpCodeBlock,
  GloSharpResultMap,
} from './transformer.js'
export { remarkGloSharp, satteriGloSharp, transformerGloSharp, selectBlock } from './markdown.js'
export type { GloSharpMarkdownOptions, GloSharpShikiOptions } from './markdown.js'
export { applyGloSharp, filterCompletions } from './render.js'
export type { GloSharpRenderOptions, RenderTarget } from './render.js'
// Re-exported so integrations share one key function with the bridge.
export { snippetKey, canonicalizeSnippet, hasGloSharpMarkers } from '@glosharp/core'
