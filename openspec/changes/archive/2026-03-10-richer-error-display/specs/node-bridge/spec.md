## MODIFIED Requirements

### Requirement: TypeScript type definitions
The package SHALL export TypeScript interfaces for `TwohashResult`, `TwohashHover`, `TwohashError`, `TwohashMeta`, and `TwohashDisplayPart` matching the JSON output schema. The `TwohashError` interface SHALL include optional `endLine` (number) and `endCharacter` (number) fields for multi-line diagnostic spans.

#### Scenario: Type-safe access
- **WHEN** a consumer accesses `result.hovers[0].parts[0].kind`
- **THEN** TypeScript provides autocompletion and type checking for all fields

#### Scenario: Type-safe multi-line error access
- **WHEN** a consumer accesses `result.errors[0].endLine`
- **THEN** TypeScript types the field as `number | undefined`
