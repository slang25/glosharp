## ADDED Requirements

### Requirement: Factory function creates twohash instance
The package SHALL export a `createTwohash()` function that accepts configuration options and returns a twohash instance with a `process()` method.

#### Scenario: Create default instance
- **WHEN** `createTwohash()` is called with no options
- **THEN** a twohash instance is returned that auto-detects the `twohash` CLI on PATH

#### Scenario: Custom executable path
- **WHEN** `createTwohash({ executable: '/path/to/twohash' })` is called
- **THEN** the instance uses the specified path to spawn the CLI

### Requirement: Process method invokes CLI and returns typed result
The `process()` method SHALL spawn the CLI as a child process, pass source code, and return a parsed `TwohashResult` object matching the JSON output schema.

#### Scenario: Process inline code
- **WHEN** `twohash.process({ code: 'var x = 42;\n//  ^?' })` is called
- **THEN** the CLI is spawned with the source on stdin, and the result contains typed `hovers`, `errors`, `code`, and `meta` fields

#### Scenario: Process file reference
- **WHEN** `twohash.process({ file: 'src/Example.cs' })` is called
- **THEN** the CLI is spawned with the file path argument

### Requirement: TypeScript type definitions
The package SHALL export TypeScript interfaces for `TwohashResult`, `TwohashHover`, `TwohashError`, `TwohashMeta`, and `TwohashDisplayPart` matching the JSON output schema.

#### Scenario: Type-safe access
- **WHEN** a consumer accesses `result.hovers[0].parts[0].kind`
- **THEN** TypeScript provides autocompletion and type checking for all fields

### Requirement: Cache results during build
The instance SHALL cache results by source code hash to avoid re-processing identical snippets within a single build.

#### Scenario: Duplicate snippet skips CLI
- **WHEN** `process()` is called twice with identical source code
- **THEN** the second call returns the cached result without spawning the CLI

### Requirement: Error handling for CLI failures
The package SHALL throw a typed error when the CLI is not found, exits with non-zero, or produces invalid JSON.

#### Scenario: CLI not found
- **WHEN** the `twohash` CLI is not on PATH and no custom path is configured
- **THEN** `process()` throws an error indicating the CLI was not found with installation instructions

#### Scenario: CLI exits with error
- **WHEN** the CLI exits with non-zero code
- **THEN** `process()` throws an error containing stderr output
