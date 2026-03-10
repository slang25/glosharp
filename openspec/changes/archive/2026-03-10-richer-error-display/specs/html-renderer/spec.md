## MODIFIED Requirements

### Requirement: Render error annotations
The renderer SHALL wrap error spans in `<span class="twohash-error-underline twohash-severity-{severity}">` elements, using severity-specific wavy underline colors (red for error, yellow/amber for warning, blue for info). For each error, a `<div class="twohash-error-message twohash-severity-{severity}">` SHALL be emitted containing the error message and code. When the error code matches `CS\d+`, the code SHALL be rendered as an `<a>` element linking to Microsoft docs. When a diagnostic spans multiple lines, underline styling SHALL be applied across all affected lines.

#### Scenario: Error underline and message
- **WHEN** a result has an error at line 3 with code `CS1002` and message `; expected`
- **THEN** the affected token is wrapped with `class="twohash-error-underline twohash-severity-error"` and a message div contains a linked `CS1002` followed by `; expected`

#### Scenario: Expected errors render the same as unexpected
- **WHEN** an error has `expected: true`
- **THEN** the error is rendered with the same underline and message styling (the `expected` flag does not affect rendering)

#### Scenario: Warning uses amber styling
- **WHEN** a result has a warning diagnostic
- **THEN** the underline uses the theme's warning color and the message div has `twohash-severity-warning` class

#### Scenario: Info uses blue styling
- **WHEN** a result has an info diagnostic
- **THEN** the underline uses the theme's info color and the message div has `twohash-severity-info` class

#### Scenario: Multi-line error underline
- **WHEN** a diagnostic spans from line 2, character 8 to line 4, character 12
- **THEN** underline styling is applied on line 2 from character 8 to end, on line 3 fully, and on line 4 from start to character 12

#### Scenario: Error code rendered as link
- **WHEN** a diagnostic has code `CS0246`
- **THEN** the error code in the message div is an `<a>` linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246`

## ADDED Requirements

### Requirement: Theme includes warning and info colors
The `TwohashTheme` SHALL include `WarningColor`, `WarningBackground`, `InfoColor`, and `InfoBackground` properties. Built-in themes SHALL define these as: github-dark warning `#d29922`/`rgba(210,153,34,0.15)`, info `#539bf5`/`rgba(83,155,245,0.15)`; github-light warning `#9a6700`/`rgba(154,103,0,0.15)`, info `#0969da`/`rgba(9,105,218,0.15)`.

#### Scenario: Github-dark theme warning colors
- **WHEN** rendering with the `github-dark` theme
- **THEN** warning underlines use `#d29922` and warning message backgrounds use `rgba(210,153,34,0.15)`

#### Scenario: Github-light theme info colors
- **WHEN** rendering with the `github-light` theme
- **THEN** info underlines use `#0969da` and info message backgrounds use `rgba(9,105,218,0.15)`
