## ADDED Requirements

### Requirement: Severity-specific visual styling
All renderers SHALL use distinct colors for each diagnostic severity level: error (red), warning (yellow/amber), and info (blue). The wavy underline color, error message border/background, and error code text SHALL all reflect the severity of the diagnostic.

#### Scenario: Warning diagnostic styled differently from error
- **WHEN** a diagnostic with `severity: "warning"` is rendered
- **THEN** the underline, message background, and code text use yellow/amber colors instead of red

#### Scenario: Info diagnostic styled differently from error
- **WHEN** a diagnostic with `severity: "info"` is rendered
- **THEN** the underline, message background, and code text use blue colors instead of red

#### Scenario: Error diagnostic retains existing red styling
- **WHEN** a diagnostic with `severity: "error"` is rendered
- **THEN** the underline, message background, and code text use the existing red color scheme

#### Scenario: Hidden diagnostics not rendered
- **WHEN** a diagnostic with `severity: "hidden"` exists in the result
- **THEN** no visual annotation is rendered for that diagnostic

### Requirement: Severity CSS class on error elements
Error-related HTML elements SHALL include a CSS class indicating severity: `glosharp-severity-error`, `glosharp-severity-warning`, or `glosharp-severity-info`. This enables custom CSS overrides by consumers.

#### Scenario: Warning underline has severity class
- **WHEN** a warning diagnostic underline is rendered
- **THEN** the underline element has class `glosharp-severity-warning` in addition to `glosharp-error-underline`

#### Scenario: Error message div has severity class
- **WHEN** an error message div is rendered for a warning
- **THEN** the message div has class `glosharp-severity-warning` in addition to `glosharp-error-message`
