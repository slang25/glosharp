## ADDED Requirements

### Requirement: Add custom tag annotations in annotateCode hook
The `annotateCode` hook SHALL create `GloSharpCustomTagAnnotation` instances for each tag in the glosharp result, rendering a callout box below the associated code line.

#### Scenario: Tag annotation created
- **WHEN** the glosharp result contains a tag with `name: "log"` at line 2
- **THEN** a `GloSharpCustomTagAnnotation` is added to line 2 with the tag name and message

#### Scenario: Multiple tag annotations
- **WHEN** the glosharp result contains tags on different lines
- **THEN** each line receives its own tag annotation with the correct name and message

### Requirement: Custom tag callout rendering
The `GloSharpCustomTagAnnotation` SHALL render as a block-level callout box below the code line. The box SHALL contain: an SVG icon specific to the tag type, the tag name as a title, and the message text as content. The rendered structure SHALL use the CSS classes `glosharp-tag` (base) and `glosharp-tag-{name}` (tag-specific).

#### Scenario: Log callout rendered
- **WHEN** a `log` tag annotation renders
- **THEN** the output contains a callout box with an info icon, "log" title, and the message text, with CSS class `glosharp-tag-log`

#### Scenario: Warn callout rendered
- **WHEN** a `warn` tag annotation renders
- **THEN** the output contains a callout box with a warning icon, "warn" title, and the message text, with CSS class `glosharp-tag-warn`

#### Scenario: Error callout rendered
- **WHEN** an `error` tag annotation renders
- **THEN** the output contains a callout box with an error icon, "error" title, and the message text, with CSS class `glosharp-tag-error`

#### Scenario: Annotate callout rendered
- **WHEN** an `annotate` tag annotation renders
- **THEN** the output contains a callout box with a lightbulb icon, "annotate" title, and the message text, with CSS class `glosharp-tag-annotate`

### Requirement: Theme-aware styling for custom tag callouts
The plugin SHALL define theme-aware CSS for custom tag callouts with tag-specific colors: log (blue), warn (amber), error (red), annotate (purple). Each tag type SHALL have distinct background, border, and icon colors. Styles SHALL use CSS custom properties with dark theme defaults as fallback values.

#### Scenario: Dark theme tag styling
- **WHEN** the EC instance uses a dark theme
- **THEN** tag callout boxes use dark-appropriate background and border colors

#### Scenario: Light theme tag styling
- **WHEN** the EC instance uses a light theme
- **THEN** tag callout boxes use light-appropriate background and border colors via `[data-theme="light"]` selectors

### Requirement: Detect custom tag markers for processing
The marker detection logic SHALL recognize `@log:`, `@warn:`, `@error:`, and `@annotate:` markers in addition to existing markers when deciding whether to invoke glosharp processing on a code block.

#### Scenario: Block with only tag markers
- **WHEN** a C# code block contains `// @log: message` but no `^?` or `@errors` markers
- **THEN** the plugin invokes glosharp processing on the block
