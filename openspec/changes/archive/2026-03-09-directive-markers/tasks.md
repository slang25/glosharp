## 1. C# Models

- [x] 1.1 Add `TwohashHighlight` class to `Models.cs` with `Line`, `Character`, `Length`, `Kind` properties
- [x] 1.2 Change `TwohashResult.Highlights` from `List<object>` to `List<TwohashHighlight>`

## 2. Marker Parser

- [x] 2.1 Add regex patterns for `@highlight`, `@focus`, and `@diff` directives in `MarkerParser.cs`
- [x] 2.2 Add `HighlightDirective` record to capture parsed kind and target line info
- [x] 2.3 Add `List<HighlightDirective> Highlights` to `MarkerParseResult`
- [x] 2.4 Parse `@highlight` (bare and with range argument) in the main parse loop, mark as marker lines
- [x] 2.5 Parse `@focus` (bare and with range argument) in the main parse loop, mark as marker lines
- [x] 2.6 Parse `@diff: +` and `@diff: -` in the main parse loop, mark as marker lines
- [x] 2.7 Expand line-range directives (e.g., `3-5`) into per-line entries after processed-line mapping
- [x] 2.8 Remap highlight directives from original to processed line coordinates
- [x] 2.9 Update `GetCompilationCode` to exclude new directive marker lines

## 3. Processor Integration

- [x] 3.1 Wire `MarkerParseResult.Highlights` to `TwohashResult.Highlights` in `TwohashProcessor.cs`, creating `TwohashHighlight` entries with correct line, character (0), length (line content length), and kind

## 4. Node Bridge Types

- [x] 4.1 Add `TwohashHighlight` interface to `packages/twohash/src/types.ts` with `line`, `character`, `length`, `kind` fields
- [x] 4.2 Update `TwohashResult.highlights` type from `unknown[]` to `TwohashHighlight[]`

## 5. EC Plugin

- [x] 5.1 Update `TWOHASH_MARKER_REGEX` in `plugin.ts` to detect `@highlight`, `@focus`, and `@diff` markers
- [x] 5.2 Import `TwohashHighlight` type from the twohash package
- [x] 5.3 Add `TwohashHighlightAnnotation` class for highlight background rendering (whole-line)
- [x] 5.4 Add `TwohashDiffAnnotation` class for diff add/remove line backgrounds (green/red)
- [x] 5.5 Add focus dimming logic: when focus entries exist, add dim annotation to non-focused lines
- [x] 5.6 Wire highlight/focus/diff annotations in the `annotateCode` hook
- [x] 5.7 Add CSS for `.twohash-highlight`, `.twohash-focus-dim`, `.twohash-diff-add`, `.twohash-diff-remove` with theme-aware colors
- [x] 5.8 Add highlight/focus/diff `styleSettings` entries for dark/light theme colors

## 6. Tests

- [x] 6.1 Add marker parser tests for `@highlight` (bare, single line, range)
- [x] 6.2 Add marker parser tests for `@focus` (bare, range)
- [x] 6.3 Add marker parser tests for `@diff: +` and `@diff: -`
- [x] 6.4 Add test for directive markers coexisting with hover and error markers
- [x] 6.5 Add integration test verifying `TwohashResult.Highlights` is populated with correct entries
