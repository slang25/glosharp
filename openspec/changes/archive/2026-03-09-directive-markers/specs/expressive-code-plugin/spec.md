## ADDED Requirements

### Requirement: Detect directive markers for processing
The marker detection logic SHALL recognize `@highlight`, `@focus`, and `@diff` markers in addition to existing markers when deciding whether to invoke twohash processing on a code block.

#### Scenario: Block with only highlight markers
- **WHEN** a C# code block contains `// @highlight` but no `^?` or `@errors` markers
- **THEN** the plugin invokes twohash processing on the block

#### Scenario: Block with only diff markers
- **WHEN** a C# code block contains `// @diff: +` but no other twohash markers
- **THEN** the plugin invokes twohash processing on the block

### Requirement: Add highlight annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashHighlightAnnotation` instances for each highlight entry with `kind: "highlight"`, applying a background color to the entire line.

#### Scenario: Highlight annotation created
- **WHEN** the twohash result contains a highlight with `kind: "highlight"` at line 2
- **THEN** a line-level annotation is added to line 2 that renders a highlight background

#### Scenario: Highlight annotation rendering
- **WHEN** the annotation renders in the EC pipeline
- **THEN** the line has a visible background color distinguishing it from non-highlighted lines

### Requirement: Add focus annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashFocusAnnotation` instances for focus presentation. Lines with `kind: "focus"` SHALL remain at full opacity. All other lines in the code block SHALL be dimmed when any focus entries exist.

#### Scenario: Focus annotation dims non-focused lines
- **WHEN** the twohash result contains focus entries for lines 2 and 3 in a 5-line block
- **THEN** lines 0, 1, and 4 are rendered with reduced opacity, while lines 2 and 3 remain at full opacity

#### Scenario: No focus entries means no dimming
- **WHEN** the twohash result contains no focus entries
- **THEN** all lines render at full opacity (no dimming applied)

### Requirement: Add diff annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashDiffAnnotation` instances for diff presentation. Lines with `kind: "add"` SHALL have a green-tinted background. Lines with `kind: "remove"` SHALL have a red-tinted background.

#### Scenario: Diff add annotation rendering
- **WHEN** the twohash result contains a highlight with `kind: "add"` at line 3
- **THEN** line 3 is rendered with a green-tinted background color

#### Scenario: Diff remove annotation rendering
- **WHEN** the twohash result contains a highlight with `kind: "remove"` at line 4
- **THEN** line 4 is rendered with a red-tinted background color

### Requirement: Theme-aware styling for highlight, focus, and diff
The plugin SHALL define `styleSettings` for highlight background, focus dimmed opacity, diff add background, and diff remove background colors that adapt to EC's light/dark theme switching.

#### Scenario: Highlight in dark theme
- **WHEN** the EC instance uses a dark theme
- **THEN** highlighted lines use a dark-appropriate background color

#### Scenario: Diff colors in light theme
- **WHEN** the EC instance uses a light theme
- **THEN** diff add lines use a light green background and diff remove lines use a light red background
