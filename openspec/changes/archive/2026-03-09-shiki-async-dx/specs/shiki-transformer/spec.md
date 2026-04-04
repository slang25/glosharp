## REMOVED Requirements

### Requirement: Export convenience transformer factory
**Reason**: `transformerGloSharp()` cannot work because Shiki's `preprocess` hook is synchronous but glosharp CLI spawning is async. Its `preprocess` returns `undefined`, meaning markers are never stripped and results are never injected. Replaced by the explicit two-step pattern: `processGloSharpBlocks()` → `transformerGloSharpFromMap()`.
**Migration**: Replace `transformerGloSharp(options)` with:
```typescript
const resultMap = await processGloSharpBlocks(codeBlocks, options)
const transformer = transformerGloSharpFromMap(resultMap)
```
