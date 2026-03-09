## 1. MarkerParser — parse new markers

- [x] 1.1 Add `[GeneratedRegex]` patterns for `// @langVersion: <value>` and `// @nullable: <value>` in MarkerParser
- [x] 1.2 Add `LangVersion` (string?) and `Nullable` (string?) fields to `MarkerParseResult`
- [x] 1.3 Detect and record `@langVersion` / `@nullable` lines during parsing, mark them as marker lines (stripped from output), normalize values to lowercase, last-one-wins for duplicates
- [x] 1.4 Write MarkerParser unit tests: parse both markers, case insensitivity, last-one-wins, lines stripped from output, position offset map accounts for removed lines

## 2. Value mapping and validation

- [x] 2.1 Create helper methods to map langVersion string → `LanguageVersion` enum and nullable string → `NullableContextOptions` enum, returning null for invalid values
- [x] 2.2 Write unit tests for value mapping: all valid numeric versions, named versions (latest/preview/default), all nullable options, invalid values return null

## 3. TwohashProcessor — apply parsed values

- [x] 3.1 Read `LangVersion` and `Nullable` from `MarkerParseResult` in `ProcessWithContextAsync`, map to enum values, use them instead of hardcoded `LanguageVersion.Latest` / `NullableContextOptions.Enable` (lines 111, 162)
- [x] 3.2 Do the same in `ExtractCompletions` (lines 255-256) so `^|` markers also respect the settings
- [x] 3.3 When mapping fails (invalid value), add a synthetic error entry to the errors list with a message listing valid options; skip compilation
- [x] 3.4 Write integration tests: snippet with `@langVersion: 7` rejects modern features, snippet with `@nullable: disable` produces no nullable warnings, default behavior unchanged when markers absent, invalid values produce error entries

## 4. Models and JSON output

- [x] 4.1 Add `LangVersion` (string?) and `Nullable` (string?) properties to `TwohashMeta` in Models.cs, with JSON serialization configured to omit when null
- [x] 4.2 Populate `meta.LangVersion` and `meta.Nullable` from parsed marker values in TwohashProcessor
- [x] 4.3 Write tests verifying JSON output includes `langVersion` / `nullable` in meta when markers present, omits when absent

## 5. Node bridge types

- [x] 5.1 Add optional `langVersion?: string` and `nullable?: string` fields to `TwohashMeta` interface in `packages/twohash/src/types.ts`
