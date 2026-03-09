## ADDED Requirements

### Requirement: Parse file-based app directive lines
The system SHALL recognize lines starting with `#:` as directive marker lines. These lines SHALL be removed from processed output and excluded from compilation code. The position offset map SHALL account for removed `#:` lines.

#### Scenario: Directive line identified as marker
- **WHEN** a line starts with `#:` (e.g., `#:package Newtonsoft.Json@13.0.3`)
- **THEN** the line is treated as a marker line, removed from processed output, and excluded from compilation code

#### Scenario: Position adjustment after directive removal
- **WHEN** two `#:` directive lines precede code starting at source line 3
- **THEN** hover and error positions in the output reference line 0 for the first code line

#### Scenario: Directives coexist with comment markers
- **WHEN** source contains `#:package` directives followed by code with `// ^?` hover queries
- **THEN** both directive stripping and hover query extraction produce correct positions in the processed output
