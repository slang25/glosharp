## Context

Twohash hardcodes `LanguageVersion.Latest` and `NullableContextOptions.Enable` in `TwohashProcessor.cs` (lines 111 and 162, repeated at lines 255-256 for completions). All snippets compile under the same settings regardless of authorial intent.

The existing `MarkerParser` already handles `// @directive` comment markers (highlight, focus, diff, errors, noErrors) by regex-matching, recording metadata, and stripping the line from output. This is the natural extension point.

## Goals / Non-Goals

**Goals:**
- Per-snippet language version control via `// @langVersion: <value>`
- Per-snippet nullable context control via `// @nullable: <value>`
- Parsed values flow through to `CSharpParseOptions` and `CSharpCompilationOptions`
- Current defaults preserved (Latest, Enable) when markers absent
- Invalid values produce clear diagnostics
- JSON meta reflects active settings for downstream consumers

**Non-Goals:**
- Config-file-based defaults (that's roadmap item 8, `.twohashrc`)
- CLI flags for language version / nullable (defer to config file)
- Supporting these via `#:property` file-based app directives (those are SDK-level; these are twohash-level markers)

## Decisions

### 1. Use `// @langVersion` and `// @nullable` comment markers (not `#:property`)

**Alternatives considered:**
- **(a)** `#:property LanguageVersion=12` — reuses file-based app directive syntax
- **(b)** `// @langVersion: 12` / `// @nullable: enable` — new twohash comment markers

**Decision: (b) Comment markers**

Rationale: `#:property` directives are passed to the .NET SDK's MSBuild system and affect `dotnet build` behavior. Language version and nullable context are Roslyn compilation settings that twohash controls directly. Using twohash's own `// @` marker convention keeps the two systems separate and works for all resolution tiers (not just file-based apps). It also matches the existing pattern for `// @errors`, `// @highlight`, etc.

### 2. Markers are per-snippet, not per-line

Each marker sets the value for the entire snippet. If multiple `// @langVersion` lines appear, the last one wins (consistent with how `// @errors` works — simple, predictable). These markers can appear anywhere in the source but are logically snippet-level configuration.

### 3. Value mapping

`// @langVersion` values map to Roslyn's `LanguageVersion` enum:
- Numeric: `"7"` → `CSharp7`, `"7.1"` → `CSharp7_1`, ..., `"12"` → `CSharp12`, `"13"` → `CSharp13`
- Named: `"latest"` → `Latest`, `"preview"` → `Preview`, `"default"` → `Default`
- Case-insensitive matching

`// @nullable` values map to `NullableContextOptions`:
- `"enable"` → `Enable`, `"disable"` → `Disable`, `"warnings"` → `Warnings`, `"annotations"` → `Annotations`
- Case-insensitive matching

Invalid values produce a twohash-level error in the `errors` array (not a Roslyn diagnostic), with a clear message listing valid options.

### 4. Meta output fields

`TwohashMeta` gains two optional string fields:
- `langVersion`: the resolved language version string (e.g., `"12"`, `"latest"`), or null when using the default
- `nullable`: the resolved nullable context string (e.g., `"enable"`, `"disable"`), or null when using the default

These are the authored values, not the Roslyn enum names — downstream consumers see what the author wrote.

## Risks / Trade-offs

- **Last-one-wins for duplicate markers** → Simple but could mask author mistakes. Mitigation: this matches existing marker behavior and is easy to understand.
- **No validation that requested language version is supported by the SDK** → Roslyn will produce its own diagnostics if the version isn't available. Mitigation: Roslyn's errors are already surfaced in the errors array.
