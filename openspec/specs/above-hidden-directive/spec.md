### Requirement: Parse @above-hidden directive
The system SHALL recognize `// @above-hidden` as a directive that hides all code above it from the output while including it in compilation. This directive SHALL behave identically to `// ---cut---`.

#### Scenario: Setup code hidden by @above-hidden
- **WHEN** source contains `using System.Text;\nvar setup = 1;\n// @above-hidden\nvar visible = 2;`
- **THEN** the output code contains only `var visible = 2;`, but compilation includes all lines

#### Scenario: @above-hidden directive line is removed from output
- **WHEN** source contains `// @above-hidden`
- **THEN** the directive line itself does not appear in the output code or compilation code

#### Scenario: @above-hidden with leading whitespace
- **WHEN** source contains `  // @above-hidden` (indented)
- **THEN** the directive is recognized and behaves the same as without indentation

### Requirement: Both ---cut--- and @above-hidden are supported simultaneously
The system SHALL accept both `// ---cut---` and `// @above-hidden` as equivalent directives. Only the first occurrence of either SHALL take effect.

#### Scenario: ---cut--- still works
- **WHEN** source contains `var setup = 1;\n// ---cut---\nvar visible = 2;`
- **THEN** the output code contains only `var visible = 2;` (unchanged behavior)

#### Scenario: Only first cut/above-hidden marker takes effect
- **WHEN** source contains both `// @above-hidden` and `// ---cut---` on different lines
- **THEN** only the first occurrence determines where the hidden section ends
