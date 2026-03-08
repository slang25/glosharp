## ADDED Requirements

### Requirement: Add completion annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashCompletionAnnotation` instances for each completion result, rendering a completion list dropdown below the queried line.

#### Scenario: Completion annotation created
- **WHEN** the twohash result contains completions at line 2, character 8
- **THEN** a `TwohashCompletionAnnotation` is added to line 2 with the completion items

#### Scenario: Completion list rendering
- **WHEN** the annotation renders in the EC pipeline
- **THEN** a styled completion list appears below the code line, showing item labels with kind indicators

### Requirement: Detect completion markers for processing
The marker detection logic SHALL recognize `^|` markers in addition to `^?` markers when deciding whether to invoke twohash processing on a code block.

#### Scenario: Block with only completion markers
- **WHEN** a C# code block contains `^|` markers but no `^?` markers
- **THEN** the plugin invokes twohash processing on the block

### Requirement: Theme-aware completion list styling
The completion list SHALL use `styleSettings` for colors that adapt to the EC theme, consistent with the existing popup styling.

#### Scenario: Completion list in dark theme
- **WHEN** the EC instance uses a dark theme
- **THEN** the completion list uses dark theme background and foreground colors from styleSettings
