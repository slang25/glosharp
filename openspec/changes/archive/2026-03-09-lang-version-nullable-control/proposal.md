## Why

Twohash currently hardcodes `LanguageVersion.Latest` and `NullableContextOptions.Enable` for all snippets. Documentation authors need per-snippet control to demonstrate language-version-specific features (e.g., showing what code looks like under C# 10 vs 13) and to show nullable-disabled vs nullable-enabled behavior — both common topics in C# educational content.

## What Changes

- Add `// @langVersion: <value>` marker to set the C# language version per snippet (e.g., `12`, `13`, `latest`, `preview`)
- Add `// @nullable: <value>` marker to control the nullable context per snippet (`enable`, `disable`, `warnings`, `annotations`)
- Both markers are parsed by MarkerParser, stripped from output, and applied to Roslyn's `CSharpParseOptions` and `CSharpCompilationOptions`
- Invalid values produce a compiler diagnostic in the output
- Add `langVersion` and `nullable` fields to `TwohashMeta` in JSON output
- Update Node bridge TypeScript types to include new meta fields

## Capabilities

### New Capabilities
- `lang-version-nullable-control`: Parsing of `// @langVersion` and `// @nullable` markers, validation, application to compilation options, and propagation through JSON output and Node bridge types

### Modified Capabilities
- `marker-parsing`: MarkerParser gains two new directive types (`@langVersion`, `@nullable`) that are stripped from output
- `json-output`: TwohashMeta gains `langVersion` and `nullable` fields

## Impact

- **Core**: `MarkerParser.cs` (new regex patterns, new fields on `MarkerParseResult`), `TwohashProcessor.cs` (read parsed values instead of hardcoded constants), `Models.cs` (`TwohashMeta` new fields)
- **Node bridge**: `types.ts` (`TwohashMeta` interface gains optional `langVersion` and `nullable` fields)
- **CLI**: No changes needed — JSON output automatically reflects model changes
- **Tests**: New tests for marker parsing and end-to-end compilation option application
