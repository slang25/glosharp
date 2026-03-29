## ADDED Requirements

### Requirement: Config-level langVersion default
The system SHALL read the `langVersion` string property from `twohash.config.json` and use it as the default language version for all code blocks. Per-block `@langVersion` markers SHALL override the config value.

#### Scenario: Config sets langVersion baseline
- **WHEN** `twohash.config.json` contains `{"langVersion": "12"}` and a code block has no `@langVersion` marker
- **THEN** the code block is compiled with C# 12 language version

#### Scenario: Per-block marker overrides config langVersion
- **WHEN** `twohash.config.json` contains `{"langVersion": "12"}` and a code block contains `// @langVersion: preview`
- **THEN** the code block is compiled with preview language version (not 12)

#### Scenario: No config langVersion uses hardcoded default
- **WHEN** `twohash.config.json` has no `langVersion` property and a code block has no `@langVersion` marker
- **THEN** the code block is compiled with `LanguageVersion.Latest` (the existing default)

### Requirement: Config-level nullable default
The system SHALL read the `nullable` string property from `twohash.config.json` and use it as the default nullable context for all code blocks. Per-block `@nullable` markers SHALL override the config value.

#### Scenario: Config sets nullable baseline
- **WHEN** `twohash.config.json` contains `{"nullable": "disable"}` and a code block has no `@nullable` marker
- **THEN** the code block is compiled with nullable context disabled

#### Scenario: Per-block marker overrides config nullable
- **WHEN** `twohash.config.json` contains `{"nullable": "disable"}` and a code block contains `// @nullable: enable`
- **THEN** the code block is compiled with nullable context enabled (not disabled)

#### Scenario: No config nullable uses hardcoded default
- **WHEN** `twohash.config.json` has no `nullable` property and a code block has no `@nullable` marker
- **THEN** the code block is compiled with `NullableContextOptions.Enable` (the existing default)
