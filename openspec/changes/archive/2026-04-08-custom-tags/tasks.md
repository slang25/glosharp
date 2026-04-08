## 1. C# Models

- [x] 1.1 Add `GloSharpTag` record to `Models.cs` with `Name`, `Text`, `Line` properties
- [x] 1.2 Add `List<GloSharpTag> Tags` to `GloSharpResult`

## 2. Marker Parser

- [x] 2.1 Add regex pattern for `@log:`, `@warn:`, `@error:`, `@annotate:` tag directives in `MarkerParser.cs`
- [x] 2.2 Add `TagDirective` record to capture parsed tag name, message text, and target line
- [x] 2.3 Add `List<TagDirective> Tags` to `MarkerParseResult`
- [x] 2.4 Parse tag directives in the main parse loop, mark as marker lines, extract name and message
- [x] 2.5 Disambiguate `@error:` tag from `@errors:` expected-error directive
- [x] 2.6 Remap tag line positions from original to processed line coordinates (targeting preceding code line)
- [x] 2.7 Update `GetCompilationCode` to exclude tag marker lines

## 3. Processor Integration

- [x] 3.1 Wire `MarkerParseResult.Tags` to `GloSharpResult.Tags` in `GloSharpProcessor.cs`, creating `GloSharpTag` entries with correct name, text, and line

## 4. Node Bridge Types

- [x] 4.1 Add `GloSharpTag` interface to `packages/glosharp/src/types.ts` with `name`, `text`, `line` fields
- [x] 4.2 Update `GloSharpResult.tags` type from `unknown[]` (or add new field) to `GloSharpTag[]`

## 5. EC Plugin

- [x] 5.1 Update `TWOHASH_MARKER_REGEX` in `plugin.ts` to detect `@log:`, `@warn:`, `@error:`, `@annotate:` markers
- [x] 5.2 Import `GloSharpTag` type from the glosharp package
- [x] 5.3 Add `GloSharpCustomTagAnnotation` class that renders callout box with icon, title, and message
- [x] 5.4 Add SVG icons for log (info), warn (warning triangle), error (error circle), annotate (lightbulb)
- [x] 5.5 Wire tag annotations in the `annotateCode` hook
- [x] 5.6 Add CSS for `.glosharp-tag`, `.glosharp-tag-log`, `.glosharp-tag-warn`, `.glosharp-tag-error`, `.glosharp-tag-annotate` with theme-aware colors

## 6. Tests

- [x] 6.1 Add marker parser tests for `@log:`, `@warn:`, `@error:`, `@annotate:` tag parsing
- [x] 6.2 Add marker parser test that `@error:` tag is disambiguated from `@errors:` directive
- [x] 6.3 Add marker parser test for bare `@log` without message (should be ignored)
- [x] 6.4 Add test for tag directives coexisting with hover, error, and highlight markers
- [x] 6.5 Add integration test verifying `GloSharpResult.Tags` is populated with correct entries
- [x] 6.6 Add integration test verifying tag line positions account for marker removal
