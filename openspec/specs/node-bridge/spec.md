## ADDED Requirements

### Requirement: Factory function creates glosharp instance
The package SHALL export a `createGloSharp()` function that accepts configuration options and returns a glosharp instance with a `process()` method.

#### Scenario: Create default instance
- **WHEN** `createGloSharp()` is called with no options
- **THEN** a glosharp instance is returned that auto-detects the `glosharp` CLI on PATH

#### Scenario: Custom executable path
- **WHEN** `createGloSharp({ executable: '/path/to/glosharp' })` is called
- **THEN** the instance uses the specified path to spawn the CLI

### Requirement: Process method invokes CLI and returns typed result
The `process()` method SHALL spawn the CLI as a child process, pass source code, and return a parsed `GloSharpResult` object matching the JSON output schema.

#### Scenario: Process inline code
- **WHEN** `glosharp.process({ code: 'var x = 42;\n//  ^?' })` is called
- **THEN** the CLI is spawned with the source on stdin, and the result contains typed `hovers`, `errors`, `code`, and `meta` fields

#### Scenario: Process file reference
- **WHEN** `glosharp.process({ file: 'src/Example.cs' })` is called
- **THEN** the CLI is spawned with the file path argument

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

### Requirement: Cache results during build
The instance SHALL cache results by source code hash to avoid re-processing identical snippets within a single build.

#### Scenario: Duplicate snippet skips CLI
- **WHEN** `process()` is called twice with identical source code
- **THEN** the second call returns the cached result without spawning the CLI

### Requirement: Error handling for CLI failures
The package SHALL throw a typed error when the CLI is not found, exits with non-zero, or produces invalid JSON.

#### Scenario: CLI not found
- **WHEN** the `glosharp` CLI is not on PATH and no custom path is configured
- **THEN** `process()` throws an error indicating the CLI was not found with installation instructions

#### Scenario: CLI exits with error
- **WHEN** the CLI exits with non-zero code
- **THEN** `process()` throws an error containing stderr output

### Requirement: Project option in process call
The `process()` method SHALL accept a `project` option in `GloSharpProcessOptions` and pass it as `--project <path>` to the CLI.

#### Scenario: Process with project
- **WHEN** `glosharp.process({ code: '...', project: './MyProject.csproj' })` is called
- **THEN** the CLI is spawned with `--project ./MyProject.csproj` argument

#### Scenario: Process without project
- **WHEN** `glosharp.process({ code: '...' })` is called without a `project` option
- **THEN** the CLI is spawned without the `--project` argument (standalone mode)

### Requirement: No-restore option in process call
The `process()` method SHALL accept a `noRestore` boolean option and pass `--no-restore` to the CLI when true.

#### Scenario: No-restore passed to CLI
- **WHEN** `glosharp.process({ code: '...', project: './MyProject.csproj', noRestore: true })` is called
- **THEN** the CLI is spawned with both `--project` and `--no-restore` arguments

### Requirement: Region option in process call
The `process()` method SHALL accept a `region` option in `GloSharpProcessOptions` and pass it as `--region <name>` to the CLI.

#### Scenario: Process with region
- **WHEN** `glosharp.process({ file: 'src/Example.cs', region: 'getting-started' })` is called
- **THEN** the CLI is spawned with `--region getting-started` argument

#### Scenario: Process without region
- **WHEN** `glosharp.process({ file: 'src/Example.cs' })` is called without a `region` option
- **THEN** the CLI is spawned without the `--region` argument

### Requirement: TypeScript types include completion structures
The package SHALL export `GloSharpCompletion` and `GloSharpCompletionItem` interfaces. `GloSharpResult.completions` SHALL be typed as `GloSharpCompletion[]` instead of `object[]`.

#### Scenario: Type-safe completion access
- **WHEN** a consumer accesses `result.completions[0].items[0].label`
- **THEN** TypeScript provides autocompletion and type checking for completion fields

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

### Requirement: TypeScript types include highlight structure
The package SHALL export a `GloSharpHighlight` interface with `line` (number), `character` (number), `length` (number), and `kind` (`'highlight' | 'focus' | 'add' | 'remove'`). `GloSharpResult.highlights` SHALL be typed as `GloSharpHighlight[]` instead of `unknown[]`.

#### Scenario: Type-safe highlight access
- **WHEN** a consumer accesses `result.highlights[0].kind`
- **THEN** TypeScript provides autocompletion with values `'highlight'`, `'focus'`, `'add'`, `'remove'`

#### Scenario: Type-safe highlight line access
- **WHEN** a consumer accesses `result.highlights[0].line`
- **THEN** TypeScript types the field as `number`

### Requirement: Cache-dir option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `cacheDir` property specifying a directory for disk-based result caching, applied to all `process()` calls on the instance.

#### Scenario: Instance-level cache directory
- **WHEN** `createGloSharp({ cacheDir: '.glosharp-cache' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--cache-dir .glosharp-cache`

### Requirement: Cache-dir option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `cacheDir` property that overrides the instance-level `cacheDir` for a single call.

#### Scenario: Per-call cache directory override
- **WHEN** `glosharp.process({ code: '...', cacheDir: './other-cache' })` is called on an instance with a different `cacheDir`
- **THEN** the CLI is spawned with `--cache-dir ./other-cache`

#### Scenario: Per-call cache directory without instance default
- **WHEN** `glosharp.process({ code: '...', cacheDir: '.glosharp-cache' })` is called on an instance without `cacheDir`
- **THEN** the CLI is spawned with `--cache-dir .glosharp-cache`

#### Scenario: No cache directory
- **WHEN** `glosharp.process({ code: '...' })` is called on an instance without `cacheDir`
- **THEN** the CLI is spawned without `--cache-dir` (existing behavior unchanged)

### Requirement: Config file option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `configFile` property specifying an explicit path to a `glosharp.config.json` file.

#### Scenario: Explicit config file passed to CLI
- **WHEN** `createGloSharp({ configFile: './glosharp.config.json' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--config ./glosharp.config.json`

#### Scenario: No config file specified
- **WHEN** `createGloSharp()` is called without `configFile` and `process()` is invoked
- **THEN** the CLI is spawned without `--config` (auto-discovery handled by CLI)

### Requirement: Config file option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `configFile` property that overrides the instance-level `configFile` for a single call.

#### Scenario: Per-call config override
- **WHEN** `glosharp.process({ code: '...', configFile: './other.json' })` is called on an instance with a different `configFile`
- **THEN** the CLI is spawned with `--config ./other.json`

#### Scenario: Per-call config without instance default
- **WHEN** `glosharp.process({ code: '...', configFile: './custom.json' })` is called on an instance without `configFile`
- **THEN** the CLI is spawned with `--config ./custom.json`

### Requirement: TypeScript types include tag structure
The package SHALL export a `GloSharpTag` interface with `name` (`'log' | 'warn' | 'error' | 'annotate'`), `text` (string), and `line` (number). `GloSharpResult.tags` SHALL be typed as `GloSharpTag[]`.

#### Scenario: Type-safe tag kind
- **WHEN** a consumer accesses `result.tags[0].name`
- **THEN** TypeScript restricts the value to the union `'log' | 'warn' | 'error' | 'annotate'`

#### Scenario: Type-safe tag line
- **WHEN** a consumer accesses `result.tags[0].line`
- **THEN** TypeScript types the field as `number`
