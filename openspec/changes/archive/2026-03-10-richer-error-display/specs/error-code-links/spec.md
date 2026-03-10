## ADDED Requirements

### Requirement: Clickable error codes linking to Microsoft docs
When an error code matches the pattern `CS` followed by digits (e.g., `CS1002`, `CS0246`), renderers SHALL wrap the error code in an `<a>` element linking to `https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/{code}` where `{code}` is the lowercased error code (e.g., `cs1002`).

#### Scenario: CS error code rendered as link
- **WHEN** a diagnostic with code `CS1002` is rendered
- **THEN** the error code is wrapped in `<a href="https://learn.microsoft.com/en-us/dotnet/csharp/language-reference/compiler-messages/cs1002" target="_blank" rel="noopener">CS1002</a>`

#### Scenario: Non-CS error code rendered as plain text
- **WHEN** a diagnostic with code `CA1234` (analyzer code) is rendered
- **THEN** the error code is rendered as plain text without a link

#### Scenario: Link opens in new tab
- **WHEN** a user clicks on a linked error code
- **THEN** the link opens in a new tab (`target="_blank"`) with `rel="noopener"`

### Requirement: Error code link styling
Linked error codes SHALL be styled to be visually recognizable as links while fitting the code block aesthetic. The link color SHALL match the diagnostic severity color. Underline decoration on the link SHALL only appear on hover.

#### Scenario: Link color matches severity
- **WHEN** a warning diagnostic code is rendered as a link
- **THEN** the link color matches the warning (yellow/amber) severity color

#### Scenario: Link underline on hover only
- **WHEN** an error code link is not being hovered
- **THEN** the link has no underline text decoration
- **WHEN** the error code link is hovered
- **THEN** the link shows an underline text decoration
