## ADDED Requirements

### Requirement: TypeScript types include structured doc comment
The package SHALL export `GloSharpDocComment`, `GloSharpDocParam`, and `GloSharpDocException` interfaces. `GloSharpHover.docs` SHALL be typed as `GloSharpDocComment | null` instead of `string | null`.

#### Scenario: Type-safe docs access
- **WHEN** a consumer accesses `result.hovers[0].docs?.summary`
- **THEN** TypeScript provides autocompletion and type checking for all doc comment fields

#### Scenario: Type-safe param access
- **WHEN** a consumer accesses `result.hovers[0].docs?.params[0].name`
- **THEN** TypeScript provides autocompletion for `name` and `text` fields

#### Scenario: Type-safe exception access
- **WHEN** a consumer accesses `result.hovers[0].docs?.exceptions[0].type`
- **THEN** TypeScript provides autocompletion for `type` and `text` fields
