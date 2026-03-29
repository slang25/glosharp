## MODIFIED Requirements

### Requirement: Parse cut markers
The system SHALL recognize both `// ---cut---` and `// @above-hidden` to split source into visible and hidden sections. Code before the first occurrence of either marker SHALL be hidden from output but included in compilation.

#### Scenario: Setup code hidden by cut
- **WHEN** source contains setup code followed by `// ---cut---` followed by display code
- **THEN** the output `code` contains only the display code, but compilation includes all code

#### Scenario: Setup code hidden by @above-hidden
- **WHEN** source contains setup code followed by `// @above-hidden` followed by display code
- **THEN** the output `code` contains only the display code, but compilation includes all code
