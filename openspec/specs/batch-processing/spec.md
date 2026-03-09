## ADDED Requirements

### Requirement: Export batch processing function
The package SHALL export a `processTwohashBlocks(blocks, options?)` async function that processes multiple code blocks and returns a result map.

#### Scenario: Basic batch processing
- **WHEN** `processTwohashBlocks(["var x = 42;\n//  ^?", "Console.WriteLine(x);"])` is called
- **THEN** it returns a `Map<string, TwohashResult>` containing one entry (only the block with markers), keyed by SHA256 hash of the raw code string

### Requirement: Accept string array input
The `processTwohashBlocks` function SHALL accept `string[]` as the first argument, treating each string as a code block to process with shared options.

#### Scenario: Array of strings
- **WHEN** `processTwohashBlocks(["code1\n// ^?", "code2\n// ^?"], { project: "./My.csproj" })` is called
- **THEN** both blocks are processed using the shared `project` option

### Requirement: Accept code block object array input
The `processTwohashBlocks` function SHALL accept `TwohashCodeBlock[]` as the first argument, where `TwohashCodeBlock` is `{ code: string; project?: string; region?: string }`. Per-block `project` and `region` override the shared options.

#### Scenario: Mixed string and object entries
- **WHEN** `processTwohashBlocks([{ code: "code1\n// ^?", project: "./A.csproj" }, "code2\n// ^?"], { project: "./B.csproj" })` is called
- **THEN** the first block uses project `./A.csproj` and the second uses `./B.csproj`

### Requirement: Skip blocks without markers
The function SHALL skip code blocks that do not contain twohash markers (`^?`, `^|`, `@errors:`, `@noErrors`, `---cut---`, `@hide`, `@show`). Skipped blocks SHALL NOT appear in the result map.

#### Scenario: No markers
- **WHEN** `processTwohashBlocks(["var x = 42;", "Console.WriteLine(x);\n// ^?"])` is called
- **THEN** the result map contains one entry (only the block with `^?`)

#### Scenario: All blocks without markers
- **WHEN** `processTwohashBlocks(["var x = 42;", "Console.WriteLine(x);"])` is called
- **THEN** the result map is empty

### Requirement: Process blocks concurrently
The function SHALL process all qualifying code blocks concurrently using `Promise.all`.

#### Scenario: Concurrent processing
- **WHEN** `processTwohashBlocks` is called with 5 code blocks containing markers
- **THEN** all 5 CLI invocations are initiated concurrently (not sequentially)

### Requirement: Reuse single twohash instance
The function SHALL create a single `createTwohash()` instance for all blocks in the batch, enabling the bridge's in-memory cache to deduplicate identical code blocks.

#### Scenario: Duplicate code blocks
- **WHEN** `processTwohashBlocks(["var x = 42;\n// ^?", "var x = 42;\n// ^?"])` is called
- **THEN** the CLI is spawned only once (the second block hits the in-memory cache)

### Requirement: Accept shared options
The function SHALL accept `TransformerTwohashOptions` as the second argument, providing shared `project`, `region`, `framework`, `executable`, and `cacheDir` options applied to all blocks (unless overridden per-block).

#### Scenario: Shared framework option
- **WHEN** `processTwohashBlocks(blocks, { framework: "net9.0" })` is called
- **THEN** all CLI invocations include the `--framework net9.0` argument

### Requirement: Export TwohashCodeBlock type
The package SHALL export a `TwohashCodeBlock` type with shape `{ code: string; project?: string; region?: string }`.

#### Scenario: Type available for import
- **WHEN** a TypeScript user imports `{ TwohashCodeBlock }` from `@twohash/shiki`
- **THEN** the type is available for typing variables and function parameters

### Requirement: Export TwohashResultMap type
The package SHALL export a `TwohashResultMap` type alias for `Map<string, TwohashResult>`.

#### Scenario: Type available for import
- **WHEN** a TypeScript user imports `{ TwohashResultMap }` from `@twohash/shiki`
- **THEN** the type is available for typing the return value of `processTwohashBlocks`
