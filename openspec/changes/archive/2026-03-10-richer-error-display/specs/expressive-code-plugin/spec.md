## MODIFIED Requirements

### Requirement: Add error annotations in annotateCode hook
The `annotateCode` hook SHALL create `TwohashErrorAnnotation` instances for error underlines and error message display. Annotations SHALL carry the diagnostic severity and apply severity-specific styling: error (red), warning (yellow/amber), info (blue). When a diagnostic spans multiple lines, underline annotations SHALL be created for each affected line.

#### Scenario: Error annotation created
- **WHEN** the twohash result contains an error at line 3
- **THEN** an inline error underline annotation and a block error message annotation are added for that line

#### Scenario: Warning annotation uses amber styling
- **WHEN** the twohash result contains a warning diagnostic at line 5
- **THEN** the underline annotation uses amber/yellow decoration color and the message annotation uses amber styling

#### Scenario: Multi-line error annotation
- **WHEN** the twohash result contains a diagnostic spanning lines 2-4
- **THEN** underline annotations are created for lines 2, 3, and 4, and the error message annotation is placed on line 4

### Requirement: Theme-aware styling via styleSettings
The plugin SHALL define `styleSettings` for popup colors (background, foreground, border), error colors (underline, message background), warning colors (underline, message background), and info colors (underline, message background) that adapt to EC's light/dark theme switching.

#### Scenario: Dark theme popup styling
- **WHEN** the EC instance uses a dark theme
- **THEN** popup elements use the dark theme color variables defined in styleSettings

#### Scenario: Light theme popup styling
- **WHEN** the EC instance uses a light theme
- **THEN** popup elements use the light theme color variables

#### Scenario: Warning colors in dark theme
- **WHEN** the EC instance uses a dark theme
- **THEN** warning underlines use dark-theme amber color and warning messages use dark-theme amber background

#### Scenario: Info colors in light theme
- **WHEN** the EC instance uses a light theme
- **THEN** info underlines use light-theme blue color and info messages use light-theme blue background

## ADDED Requirements

### Requirement: Render clickable error codes in error messages
The `postprocessRenderedBlock` hook SHALL render error codes matching `CS\d+` as `<a>` elements linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{code}`. Links SHALL open in a new tab with `rel="noopener"`. Non-CS codes SHALL remain plain text.

#### Scenario: CS error code linked
- **WHEN** an error message with code `CS1002` is rendered in the EC pipeline
- **THEN** the error code is an `<a>` element with href to the Microsoft docs page and `target="_blank"`

#### Scenario: Analyzer code not linked
- **WHEN** an error message with code `CA1234` is rendered
- **THEN** the error code is plain text without a link
