### Requirement: Parse ---cut-before--- directive
The system SHALL recognize `// ---cut-before---` as a directive that hides all code above it from the output while including it in compilation. `// ---cut---` SHALL be accepted as shorthand for `// ---cut-before---`.

#### Scenario: Setup code hidden by ---cut-before---
- **WHEN** source contains `using System.Text;\nvar setup = 1;\n// ---cut-before---\nvar visible = 2;`
- **THEN** the output code contains only `var visible = 2;`, but compilation includes all lines

#### Scenario: ---cut-before--- directive line is removed from output
- **WHEN** source contains `// ---cut-before---`
- **THEN** the directive line itself does not appear in the output code or compilation code

#### Scenario: ---cut-before--- with leading whitespace
- **WHEN** source contains `  // ---cut-before---` (indented)
- **THEN** the directive is recognized and behaves the same as without indentation

### Requirement: Parse ---cut-after--- directive
The system SHALL recognize `// ---cut-after---` as a directive that hides all code below it from the output while including it in compilation.

#### Scenario: Trailing code hidden by ---cut-after---
- **WHEN** source contains `var visible = 1;\n// ---cut-after---\nvar hidden = 2;`
- **THEN** the output code contains only `var visible = 1;`, but compilation includes all lines

### Requirement: Parse ---cut-start---/---cut-end--- directives
The system SHALL recognize `// ---cut-start---` and `// ---cut-end---` as paired directives that hide a section of code from output while including it in compilation. Multiple pairs SHALL be supported. An unclosed `// ---cut-start---` SHALL hide to end of file.

#### Scenario: Hidden middle section
- **WHEN** source contains `var a = 1;\n// ---cut-start---\nvar hidden = 2;\n// ---cut-end---\nvar b = 3;`
- **THEN** the output code contains `var a = 1;\nvar b = 3;`, but compilation includes all lines

### Requirement: Both ---cut--- and ---cut-before--- are supported simultaneously
The system SHALL accept both `// ---cut---` and `// ---cut-before---` as equivalent directives. Only the first occurrence of either SHALL take effect.

#### Scenario: ---cut--- still works
- **WHEN** source contains `var setup = 1;\n// ---cut---\nvar visible = 2;`
- **THEN** the output code contains only `var visible = 2;` (unchanged behavior)

#### Scenario: Only first cut-before marker takes effect
- **WHEN** source contains both `// ---cut-before---` and `// ---cut---` on different lines
- **THEN** only the first occurrence determines where the hidden section ends

### Requirement: ---cut-before--- and ---cut-after--- can be combined
The system SHALL support using both `// ---cut-before---` (or `// ---cut---`) and `// ---cut-after---` in the same source, showing only the code between them.

#### Scenario: Show only middle section
- **WHEN** source contains `var a = 1;\n// ---cut-before---\nvar b = 2;\n// ---cut-after---\nvar c = 3;`
- **THEN** the output code contains only `var b = 2;`, but compilation includes all lines
