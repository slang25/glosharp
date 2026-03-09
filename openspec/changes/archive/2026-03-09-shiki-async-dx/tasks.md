## 1. Types and Exports

- [x] 1.1 Add `TwohashCodeBlock` type (`{ code: string; project?: string; region?: string }`) and `TwohashResultMap` type alias (`Map<string, TwohashResult>`) to `transformer.ts`
- [x] 1.2 Export `TwohashCodeBlock`, `TwohashResultMap`, `processTwohashBlocks`, and `transformerTwohashFromMap` from `index.ts`

## 2. Batch Processing

- [x] 2.1 Implement `processTwohashBlocks(blocks, options?)` in `transformer.ts` — normalize string/object input, filter by markers, create single `createTwohash()` instance, process concurrently via `Promise.all`, return `Map<string, TwohashResult>` keyed by SHA256 hash
- [x] 2.2 Add tests: basic batch processing returns map with correct entries
- [x] 2.3 Add tests: skips blocks without markers, empty input returns empty map
- [x] 2.4 Add tests: per-block project/region overrides shared options
- [x] 2.5 Add tests: duplicate code blocks hit cache (single CLI call)

## 3. Result Map Transformer

- [x] 3.1 Implement `transformerTwohashFromMap(resultMap)` in `transformer.ts` — `preprocess` hashes code and looks up result, `root` injects hovers/errors/completions using existing HAST utilities
- [x] 3.2 Add tests: transformer replaces code and injects hovers when code is in map
- [x] 3.3 Add tests: transformer is a no-op when code is not in map
- [x] 3.4 Add tests: single transformer instance handles multiple sequential `codeToHtml` calls correctly

## 4. Remove Broken API

- [x] 4.1 Remove `transformerTwohash()` function from `transformer.ts`
- [x] 4.2 Remove `transformerTwohash` export from `index.ts`
- [x] 4.3 Remove or update existing tests that reference `transformerTwohash()`
