## 1. Core Model Changes

- [x] 1.1 Add `Persistent` boolean property to `TwohashHover` in `Models.cs` (default `false`)

## 2. Roslyn Extraction — Auto-Hover

- [x] 2.1 Extract shared symbol-to-hover helper from `ExtractHovers()` in `TwohashProcessor.cs`
- [x] 2.2 Implement `ExtractAllHovers()` that walks `root.DescendantTokens()`, filters to tokens with resolved symbols, and produces hovers with `persistent: false`
- [x] 2.3 Map auto-hover positions from compilation-code to processed-code using existing `LineMap`
- [x] 2.4 Exclude tokens in hidden sections (before `---cut---` or within `@hide`/`@show`) from auto-hovers
- [x] 2.5 Deduplicate: when a `^?` persistent hover and auto-hover target the same token position, keep only the persistent hover

## 3. Roslyn Extraction — Persistent Hovers

- [x] 3.1 Update `ExtractHovers()` to set `persistent: true` on all `^?`-triggered hovers

## 4. Merge and Output

- [x] 4.1 Merge auto-hovers and persistent hovers into the `Hovers` list in `TwohashResult`
- [x] 4.2 Verify JSON serialization includes `persistent` field (camelCase: `persistent`)

## 5. Expressive Code Plugin — Rendering

- [x] 5.1 Update hover annotation to read `persistent` flag from hover data
- [x] 5.2 Render default hovers (`persistent: false`): `twohash-hover` class, popup visible only on `:hover`, no token underline
- [x] 5.3 Render persistent hovers (`persistent: true`): `twohash-hover twohash-hover-persistent` classes, popup always visible, token underline decoration
- [x] 5.4 Add CSS for `.twohash-hover-persistent` (always-visible popup, underline on token)
- [x] 5.5 Ensure default hover tokens have no visual decoration in resting state

## 6. Plugin — Processing Gate

- [x] 6.1 Remove marker-detection gate: process all C# code blocks regardless of marker presence
- [x] 6.2 Ensure non-C# code blocks continue to be skipped

## 7. Tests

- [x] 7.1 Add tests for auto-hover extraction on a simple snippet (verifies hovers for identifiers, no hovers for punctuation/literals)
- [x] 7.2 Add tests for persistent hover flag on `^?`-triggered hovers
- [x] 7.3 Add tests for deduplication (auto-hover + `^?` at same position → single persistent hover)
- [x] 7.4 Add tests for auto-hover position mapping with markers removed
- [x] 7.5 Add tests for hidden section exclusion from auto-hovers
