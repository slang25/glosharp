## ADDED Requirements

### Requirement: Multi-line diagnostic span rendering
When a diagnostic has `endLine` greater than `line`, renderers SHALL apply underline styling across all affected lines. The first line SHALL be underlined from `character` to end of line content. Middle lines SHALL be underlined across the full line content. The last line SHALL be underlined from column 0 to `endCharacter`.

#### Scenario: Two-line error span
- **WHEN** a diagnostic spans from line 3, character 10 to line 4, character 5
- **THEN** line 3 is underlined from character 10 to end of line, and line 4 is underlined from character 0 to character 5

#### Scenario: Three-line error span
- **WHEN** a diagnostic spans from line 2, character 8 to line 4, character 12
- **THEN** line 2 is underlined from character 8 to end of line, line 3 is underlined fully, and line 4 is underlined from character 0 to character 12

#### Scenario: Single-line span unchanged
- **WHEN** a diagnostic has no `endLine`/`endCharacter` (or `endLine` equals `line`)
- **THEN** rendering uses the existing single-line underline behavior based on `character` and `length`

### Requirement: Error message placement for multi-line spans
The error message div for a multi-line diagnostic SHALL appear after the last affected line (`endLine`), not the first line.

#### Scenario: Message after last line of multi-line span
- **WHEN** a diagnostic spans from line 2 to line 4
- **THEN** the error message div is rendered after line 4
