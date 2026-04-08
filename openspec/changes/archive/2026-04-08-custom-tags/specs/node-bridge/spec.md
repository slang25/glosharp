## MODIFIED Requirements

### Requirement: TypeScript type definitions
The package SHALL export TypeScript interfaces for `GloSharpResult`, `GloSharpHover`, `GloSharpError`, `GloSharpMeta`, `GloSharpDisplayPart`, and `GloSharpTag` matching the JSON output schema. The `GloSharpError` interface SHALL include optional `endLine` (number) and `endCharacter` (number) fields for multi-line diagnostic spans.

#### Scenario: Type-safe access
- **WHEN** a consumer accesses `result.hovers[0].parts[0].kind`
- **THEN** TypeScript provides autocompletion and type checking for all fields

#### Scenario: Type-safe multi-line error access
- **WHEN** a consumer accesses `result.errors[0].endLine`
- **THEN** TypeScript types the field as `number | undefined`

#### Scenario: Type-safe tag access
- **WHEN** a consumer accesses `result.tags[0].name`
- **THEN** TypeScript provides autocompletion with values `'log'`, `'warn'`, `'error'`, `'annotate'`

#### Scenario: Type-safe tag text access
- **WHEN** a consumer accesses `result.tags[0].text`
- **THEN** TypeScript types the field as `string`

## ADDED Requirements

### Requirement: TypeScript types include tag structure
The package SHALL export a `GloSharpTag` interface with `name` (`'log' | 'warn' | 'error' | 'annotate'`), `text` (string), and `line` (number). `GloSharpResult.tags` SHALL be typed as `GloSharpTag[]`.

#### Scenario: Type-safe tag kind
- **WHEN** a consumer accesses `result.tags[0].name`
- **THEN** TypeScript restricts the value to the union `'log' | 'warn' | 'error' | 'annotate'`

#### Scenario: Type-safe tag line
- **WHEN** a consumer accesses `result.tags[0].line`
- **THEN** TypeScript types the field as `number`
