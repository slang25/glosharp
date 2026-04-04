## MODIFIED Requirements

### Requirement: Inject error annotations in root hook
The `root` hook SHALL add error underline elements and error message elements for compiler diagnostics. Underline and message elements SHALL include a severity CSS class (`glosharp-severity-error`, `glosharp-severity-warning`, or `glosharp-severity-info`). Error codes matching `CS\d+` SHALL be rendered as `<a>` elements linking to Microsoft docs. When a diagnostic spans multiple lines, underline elements SHALL be applied to each affected line.

#### Scenario: Error at a position
- **WHEN** the glosharp result contains an error at line 3, character 8
- **THEN** the HAST tree contains an error underline span at that position and an error message element with the diagnostic text

#### Scenario: Warning with severity class
- **WHEN** the glosharp result contains a warning diagnostic
- **THEN** the underline and message elements have class `glosharp-severity-warning`

#### Scenario: Error code linked to docs
- **WHEN** the glosharp result contains a diagnostic with code `CS0246`
- **THEN** the error code in the message element is an `<a>` linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs0246`

#### Scenario: Multi-line diagnostic underlines
- **WHEN** the glosharp result contains a diagnostic spanning lines 2-4
- **THEN** underline elements are applied to lines 2, 3, and 4, and the error message appears after line 4
