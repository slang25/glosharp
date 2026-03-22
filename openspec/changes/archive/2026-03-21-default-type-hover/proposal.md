## Why

Currently, type information is only displayed when the author explicitly adds a `^?` marker — meaning every hover must be opted-in. This creates friction for documentation authors and misses the core value proposition: twohash-enhanced code blocks should feel like an IDE, where hovering any token reveals its type. The `^?` syntax should instead serve as a way to pin a hover permanently visible (a "persistent hover"), mirroring how IDE tooltips work vs. pinned documentation panels.

## What Changes

- **BREAKING**: All identifiers/tokens in twohash-processed code blocks will emit hover data by default — no `^?` marker required. The expressive-code plugin will render these as mouse-over popups (same visual as today's `^?` popups, but only visible on hover interaction).
- `^?` markers change meaning: instead of "query type info here", they become "display a persistent hover here" — the popup is always visible without requiring mouse interaction, styled distinctly from mouse-over popups (e.g. always-expanded, anchored below/above the token).
- The core extraction layer will gain an "extract all hovers" mode that walks the syntax tree and produces hover data for every token with semantic meaning.
- The JSON output format adds a new field or flag to distinguish persistent hovers (`^?`-triggered) from default hovers (auto-extracted).

## Capabilities

### New Capabilities
- `auto-hover-extraction`: Automatic extraction of hover/type information for all semantically meaningful tokens in a code block, without requiring explicit `^?` markers.

### Modified Capabilities
- `marker-parsing`: `^?` changes from "hover query" to "persistent hover" gesture. Parsing remains the same but the semantic meaning changes.
- `roslyn-extraction`: Extraction must support a bulk mode that walks all tokens and extracts hover data, not just at `^?`-specified positions.
- `expressive-code-plugin`: Plugin must render two kinds of hovers: default (mouse-over) for all tokens, and persistent (always visible) for `^?`-marked tokens. Detection logic changes — all C# blocks get processed, not just those with markers.

## Impact

- **Core C# (`src/TwoHash.Core/`)**: `TwohashProcessor` needs a new extraction path that walks all tokens. `MarkerParser` semantics change for `^?`. Output models need a `persistent` flag on hovers.
- **JSON output**: Hovers gain a field to distinguish persistent vs default. Significantly more hover data per snippet.
- **Expressive-code plugin**: Two rendering modes for hovers. All C# blocks now get processed (not just those with markers). CSS changes for persistent vs mouse-over styling.
- **Node bridge**: No structural changes expected, just passes through more data.
- **Performance**: Extracting hovers for every token is more expensive — may need to scope to "meaningful" tokens (identifiers, keywords with semantic info) rather than every syntax trivia.
- **Breaking change**: Existing `^?` usage will change from "show hover" to "show persistent hover". Authors may need to remove `^?` markers if they only wanted mouse-over behavior.
