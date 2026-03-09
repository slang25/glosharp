## REMOVED Requirements

### Requirement: Export convenience transformer factory
**Reason**: `transformerTwohash()` cannot work because Shiki's `preprocess` hook is synchronous but twohash CLI spawning is async. Its `preprocess` returns `undefined`, meaning markers are never stripped and results are never injected. Replaced by the explicit two-step pattern: `processTwohashBlocks()` → `transformerTwohashFromMap()`.
**Migration**: Replace `transformerTwohash(options)` with:
```typescript
const resultMap = await processTwohashBlocks(codeBlocks, options)
const transformer = transformerTwohashFromMap(resultMap)
```
