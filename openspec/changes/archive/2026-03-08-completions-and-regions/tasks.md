## 1. Completion Marker Parsing

- [x] 1.1 Add `^|` regex pattern to `MarkerParser` (alongside existing `^?` pattern)
- [x] 1.2 Add `CompletionQuery` record to `MarkerParser.cs` with `OriginalLine` and `Column` fields
- [x] 1.3 Add `CompletionQueries` list to `MarkerParseResult`
- [x] 1.4 Implement `^|` marker detection, column calculation, and line remapping in `MarkerParser.Parse()`
- [x] 1.5 Update `GetCompilationCode()` to strip `^|` marker lines
- [x] 1.6 Write unit tests for `^|` parsing: single query, multiple queries, mixed with `^?`, position remapping

## 2. Completion Extraction (Roslyn)

- [x] 2.1 Add `GloSharpCompletion` and `GloSharpCompletionItem` model classes to `Models.cs`
- [x] 2.2 Replace `List<object> Completions` with `List<GloSharpCompletion> Completions` in `GloSharpResult`
- [x] 2.3 Make `GloSharpProcessor.Process()` async (return `Task<GloSharpResult>`)
- [x] 2.4 Implement completion extraction: create `AdhocWorkspace`, add project/document, call `CompletionService.GetCompletionsAsync()` at each `^|` position
- [x] 2.5 Map `CompletionItem` results to `GloSharpCompletionItem` (label, kind from tags, detail from properties)
- [x] 2.6 Skip workspace creation when no `^|` markers are present (optimization)
- [x] 2.7 Write unit tests: completions after dot access, completions for locals in scope, empty completions at invalid position

## 3. Region Extraction

- [x] 3.1 Create `RegionExtractor` class that finds named `#region`/`#endregion` blocks and returns start/end line indices
- [x] 3.2 Add `RegionName` property to `GloSharpProcessorOptions`
- [x] 3.3 Implement region extraction in `GloSharpProcessor`: when region specified, mark lines outside the region as hidden (reuse hide/show infrastructure)
- [x] 3.4 Strip `#region` and `#endregion` lines from processed output
- [x] 3.5 Throw descriptive error when requested region is not found
- [x] 3.6 Write unit tests: extract named region, region with markers inside, region not found error, duplicate region names (first match wins), region directives hidden from output

## 4. CLI Changes

- [x] 4.1 Add `--region` argument to `process` command in `Program.cs`
- [x] 4.2 Add `--region` argument to `verify` command
- [x] 4.3 Validate `--region` is not combined with `--stdin` (exit with error)
- [x] 4.4 Pass `RegionName` to `GloSharpProcessorOptions`
- [x] 4.5 Update `Process()` calls to use `await` (async change)
- [x] 4.6 Write CLI integration tests: process with region, region not found, region with stdin rejected

## 5. Node.js Bridge Updates

- [x] 5.1 Add `GloSharpCompletion` and `GloSharpCompletionItem` interfaces to `types.ts`
- [x] 5.2 Update `GloSharpResult.completions` type from `object[]` to `GloSharpCompletion[]`
- [x] 5.3 Add `region` to `GloSharpProcessOptions` interface
- [x] 5.4 Pass `--region` argument when spawning CLI
- [x] 5.5 Write unit tests for region argument construction and completion types

## 6. Shiki Transformer Updates

- [x] 6.1 Update marker detection regex to include `^|` pattern
- [x] 6.2 Implement completion list HAST injection in `root` hook: `<ul class="glosharp-completion-list">` with `<li>` items containing label, kind badge, and detail
- [x] 6.3 Add CSS styles for completion list (dropdown appearance, kind-specific classes)
- [x] 6.4 Add `region` option passthrough to transformer options
- [x] 6.5 Write tests: completion list rendering, mixed hovers and completions, region option forwarded

## 7. Expressive Code Plugin Updates

- [x] 7.1 Update marker detection to include `^|` pattern
- [x] 7.2 Create `GloSharpCompletionAnnotation` class extending `ExpressiveCodeAnnotation` with completion list rendering
- [x] 7.3 Add completion annotation creation in `preprocessCode` hook
- [x] 7.4 Add theme-aware `styleSettings` for completion list colors
- [x] 7.5 Add `region` option passthrough to plugin options
- [x] 7.6 Write tests: completion annotation rendering, theme styling, region option forwarded

## 8. Samples and End-to-End Tests

- [x] 8.1 Create sample file `samples/completions.cs` demonstrating `^|` markers
- [x] 8.2 Create sample file `samples/regions.cs` with `#region` blocks and markers inside
- [x] 8.3 End-to-end test: source with `^|` → CLI → JSON with populated completions array
- [x] 8.4 End-to-end test: source with `--region` → CLI → JSON with only region code
- [x] 8.5 End-to-end test: completions rendered in Shiki transformer output
- [x] 8.6 End-to-end test: completions rendered in EC plugin output
