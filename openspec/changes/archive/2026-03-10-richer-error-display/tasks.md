## 1. C# Core — Data Model & Extraction

- [x] 1.1 Add `EndLine` and `EndCharacter` nullable int properties to `TwohashError` in `Models.cs`
- [x] 1.2 Add `WarningColor`, `WarningBackground`, `InfoColor`, `InfoBackground` properties to `TwohashTheme` and populate for both `github-dark` and `github-light` built-in themes
- [x] 1.3 Update diagnostic extraction in `TwohashProcessor.cs` to extract end line/character from `Diagnostic.Location` when span crosses multiple lines; omit when single-line
- [x] 1.4 Configure JSON serialization to omit null `EndLine`/`EndCharacter` fields from output

## 2. C# Core — HTML Renderer

- [x] 2.1 Update `HtmlRenderer` to select underline color based on diagnostic severity using theme's error/warning/info colors
- [x] 2.2 Add `twohash-severity-{severity}` CSS class to error underline and message elements
- [x] 2.3 Render error codes matching `CS\d+` as `<a>` elements linking to Microsoft docs URL, with `target="_blank"` and `rel="noopener"`
- [x] 2.4 Implement multi-line underline rendering: first line from character to end, middle lines full, last line from start to endCharacter
- [x] 2.5 Place error message div after the last affected line for multi-line diagnostics
- [x] 2.6 Add warning and info color CSS rules to the inline `<style>` block

## 3. TypeScript — Node Bridge Types

- [x] 3.1 Add optional `endLine?: number` and `endCharacter?: number` to `TwohashError` interface in `types.ts`

## 4. TypeScript — Expressive Code Plugin

- [x] 4.1 Add warning and info color styleSettings (underline color, message background) for both light and dark themes
- [x] 4.2 Update `TwohashErrorAnnotation` to carry severity and apply severity-specific CSS class and underline color
- [x] 4.3 Render error codes matching `CS\d+` as `<a>` elements in `postprocessRenderedBlock`
- [x] 4.4 Handle multi-line diagnostic spans: create underline annotations for each affected line, place message annotation on last line

## 5. TypeScript — Shiki Transformer

- [x] 5.1 Update `injectErrors` to add `twohash-severity-{severity}` class to underline and message elements
- [x] 5.2 Render error codes matching `CS\d+` as `<a>` elements in error message divs
- [x] 5.3 Handle multi-line diagnostic spans: apply underline elements across affected lines, place message after last line

## 6. Testing

- [x] 6.1 Add test for multi-line diagnostic extraction (source with unterminated string or multi-line expression error) verifying `endLine`/`endCharacter` populated
- [x] 6.2 Add test for single-line diagnostic verifying `endLine`/`endCharacter` are null/omitted
- [x] 6.3 Add test for JSON output shape with multi-line error fields
- [x] 6.4 Add test for HTML renderer severity-based color selection
- [x] 6.5 Add test for error code URL generation (CS codes get links, analyzer codes don't)
