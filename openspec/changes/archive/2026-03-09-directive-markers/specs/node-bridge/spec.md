## ADDED Requirements

### Requirement: TypeScript types include highlight structure
The package SHALL export a `GloSharpHighlight` interface with `line` (number), `character` (number), `length` (number), and `kind` (`'highlight' | 'focus' | 'add' | 'remove'`). `GloSharpResult.highlights` SHALL be typed as `GloSharpHighlight[]` instead of `unknown[]`.

#### Scenario: Type-safe highlight access
- **WHEN** a consumer accesses `result.highlights[0].kind`
- **THEN** TypeScript provides autocompletion with values `'highlight'`, `'focus'`, `'add'`, `'remove'`

#### Scenario: Type-safe highlight line access
- **WHEN** a consumer accesses `result.highlights[0].line`
- **THEN** TypeScript types the field as `number`
