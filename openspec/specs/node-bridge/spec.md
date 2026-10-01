# node-bridge Specification

## Purpose
The `@glosharp/core` Node API that drives the CLI.
## Requirements
### Requirement: Factory function creates glosharp instance
The package SHALL export a `createGloSharp()` function that accepts configuration options and returns a glosharp instance with `process()` and `render()` methods.

#### Scenario: Create default instance
- **WHEN** `createGloSharp()` is called with no options
- **THEN** a glosharp instance is returned that auto-detects the CLI: `$GLOSHARP_EXECUTABLE`, then `glosharp` on PATH, then `~/.dotnet/tools/glosharp`, then a local dotnet tool (`dotnet glosharp`)

#### Scenario: Custom executable path
- **WHEN** `createGloSharp({ executable: '/path/to/glosharp' })` is called
- **THEN** the instance uses the specified path to spawn the CLI

#### Scenario: Executable as a .dll or argv prefix
- **WHEN** `executable` is a path ending in `.dll`, or an array such as `['dotnet', 'GloSharp.Cli.dll']`
- **THEN** the CLI is started as `dotnet <dll> …` (respectively the given command with the given leading arguments)

### Requirement: Executable discovery is portable and memoised
Discovery SHALL split PATH on the platform delimiter (`;` on Windows) and, on Windows, try PATHEXT extensions (`.exe`/`.com` only, since Node cannot spawn `.cmd`/`.bat` without a shell). A local tool SHALL be detected from a `dotnet tool list --local` row for package `glosharp.cli` or command `glosharp`, not from a substring match. The discovery outcome SHALL be memoised per process (keyed by the explicit executable, `GLOSHARP_EXECUTABLE`, PATH, platform and working directory), so `dotnet tool list` runs at most once rather than once per snippet.

#### Scenario: Windows PATH
- **WHEN** PATH is `C:\tools;"C:\Program Files\dotnet tools"` and `glosharp.exe` exists in the second directory
- **THEN** discovery returns `C:\Program Files\dotnet tools\glosharp.exe`

#### Scenario: Many snippets
- **WHEN** 50 snippets are processed with a local-tool installation
- **THEN** `dotnet tool list` runs once

### Requirement: Process method invokes CLI and returns typed result
The `process()` method SHALL run the snippet through the CLI — on a `glosharp serve` worker (see "Serve worker pool"), or as a child process of its own — and return a parsed `GloSharpResult` object matching the JSON output schema. Both paths SHALL produce identical results.

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
The instance SHALL cache results keyed on everything that affects the output: the source code (or, for `file`, the resolved path, size and modification time), every CLI argument (framework, project, region, no-restore, cache dir, config, complog, complog project) and the working directory. The cache SHALL store the in-flight promise so concurrent identical calls share one CLI run, SHALL evict failed runs, and SHALL be bounded (LRU, `cacheSize`, default 1000). `render()` SHALL use a separate cache whose key also includes the rendering arguments (theme, standalone), and SHALL NOT share entries with `process()`.

#### Scenario: Duplicate snippet skips CLI
- **WHEN** `process()` is called twice with identical source code and options
- **THEN** the second call returns the cached result without spawning the CLI

#### Scenario: Duplicate render skips CLI
- **WHEN** `render()` is called twice with identical source and theme
- **THEN** the second call returns the cached HTML without spawning the CLI

#### Scenario: Theme is part of the render cache key
- **WHEN** `render()` is called with the same source under two different themes
- **THEN** the CLI is spawned once per theme

#### Scenario: Caches are independent
- **WHEN** `process()` and then `render()` are called with the same source
- **THEN** `render()` spawns the CLI and returns HTML rather than the cached `GloSharpResult`

#### Scenario: Same code, different options
- **WHEN** `process({ code })` is followed by `process({ code, framework: 'net10.0' })` or `process({ code, project: './B.csproj' })`
- **THEN** each call runs the CLI and returns its own result

#### Scenario: File changed on disk
- **WHEN** `process({ file })` is called, the file is edited, and `process({ file })` is called again
- **THEN** the second call runs the CLI again

### Requirement: Error handling for CLI failures
The package SHALL throw a `GloSharpCliError` (with `kind`: `not-found`, `spawn`, `exit`, `timeout`, `aborted` or `invalid-output`, plus `exitCode`, `stderr` and a snippet excerpt where available) when the CLI is not found, cannot start, exits with non-zero, times out, is aborted, or produces invalid JSON. Compile errors in a snippet are not exceptions.

#### Scenario: CLI not found
- **WHEN** the `glosharp` CLI is not on PATH and no custom path is configured
- **THEN** `process()` throws an error indicating the CLI was not found with installation instructions (global and local tool, `--prerelease`, and the `executable` / `GLOSHARP_EXECUTABLE` overrides)

#### Scenario: CLI exits with error
- **WHEN** the CLI exits with non-zero code
- **THEN** `process()` throws an error containing the exit code, stderr output and the first line of the snippet

#### Scenario: CLI exits before reading stdin
- **WHEN** the CLI exits early while a large snippet is still being written to its stdin
- **THEN** the resulting `EPIPE` is handled and `process()` rejects with the exit code and stderr; the host process does not crash

### Requirement: Bounded concurrency
Snippets in progress (one-shot CLI processes, or requests in flight on `serve` workers) SHALL be limited process-wide (shared by every instance) to `$GLOSHARP_CONCURRENCY` or `max(1, min(cpus - 1, 8))` by default, adjustable with `configureGloSharp({ concurrency })`. An instance MAY add its own lower cap with the `concurrency` option.

#### Scenario: Many snippets at once
- **WHEN** 40 snippets are processed concurrently with a limit of 4
- **THEN** at most 4 are in progress at any time

### Requirement: Serve worker pool
By default the bridge SHALL run snippets on a small pool of long-lived `glosharp serve` workers instead of one CLI process per snippet. The pool size SHALL be the `workers` option, else `configureGloSharp({ workers })`, else `$GLOSHARP_WORKERS`, else `max(1, min(2, cpus - 1))`; `0` disables the pool. Pools SHALL be shared by every instance using the same executable and size, and workers SHALL start lazily: a request goes to the least-loaded worker, and a new worker starts only while every worker is busy and the pool has room.

Requests SHALL carry the same options the one-shot command line would (plus the Node process's working directory as `cwd`), so results, config discovery and error messages are identical on both paths; responses SHALL be matched to requests by id, in any order. An error response SHALL reject with the `GloSharpCliError` the one-shot run would have produced (`kind: 'exit'`, its `exitCode` and `stderr`).

Idle workers SHALL NOT keep Node running; busy ones SHALL (as a running CLI does). Workers SHALL be killed when Node exits, and `closeGloSharpWorkers()` SHALL stop them (letting them finish their requests).

#### Scenario: Many snippets, few processes
- **WHEN** 60 snippets are processed with `workers: 2`
- **THEN** at most 2 `glosharp serve` processes start, and the results equal those of one CLI process per snippet

#### Scenario: Out-of-order responses
- **WHEN** a worker answers three requests in the reverse of the order they were sent
- **THEN** each `process()` call resolves with its own result

#### Scenario: Timeout on a worker
- **WHEN** a request does not finish within `timeoutMs`
- **THEN** the call rejects with the same `timeout` error as a one-shot run, the worker takes no new requests and is killed once its other requests are answered, and later calls start a new worker

#### Scenario: Worker crash
- **WHEN** a worker exits while requests are in flight
- **THEN** only those requests reject (`kind: 'exit'`, with the exit code and the worker's stderr), and the next call starts a new worker

#### Scenario: Unexpected output
- **WHEN** a worker writes a line to stdout that is not a protocol message
- **THEN** its in-flight requests reject with `invalid-output` and the worker is replaced

### Requirement: Fallback for CLIs without serve
When the first worker of a pool cannot serve — the CLI has no `serve` command (exit code 2, "unknown command"), its handshake is not JSON or names another protocol version, it exits before the handshake, or no handshake arrives within 30 s (`$GLOSHARP_SERVE_HANDSHAKE_TIMEOUT_MS`) — the bridge SHALL run that pool's requests as one CLI process per snippet, transparently, and log one `console.warn` naming the CLI and the reason. Alongside the first worker the bridge SHALL run `glosharp serve --help` with stdin closed, and fall back as soon as it fails or prints no `glosharp serve` usage, so a wrapper that treats every command as `process` does not wait for the handshake timeout. A CLI that cannot be started at all SHALL fall back without a warning (the one-shot run reports it).

#### Scenario: Older CLI
- **WHEN** the CLI answers `glosharp serve` with "unknown command" and exit code 2
- **THEN** every snippet is processed with one CLI run each, results are unchanged, and a single warning suggests updating the CLI

#### Scenario: Protocol mismatch
- **WHEN** the handshake carries `"protocol": 2`
- **THEN** the bridge falls back and the warning names both protocol versions

### Requirement: Timeouts and cancellation
Each CLI run SHALL be killed (the whole process tree on Windows) after `timeoutMs` (instance or per-call option, else `$GLOSHARP_TIMEOUT_MS`, else 180000; `0` disables) and the call SHALL reject with a `timeout` error naming the snippet. A per-call `signal` (AbortSignal) SHALL kill the run and reject with an `aborted` error. Running CLI processes SHALL be killed when the Node process exits. On a `serve` worker, a timed-out or aborted request rejects the same way and the worker is retired (killed once its other requests are answered).

#### Scenario: Hung restore
- **WHEN** the CLI does not finish within `timeoutMs`
- **THEN** the child is killed and `process()` rejects with a message saying it timed out and how to raise the limit

### Requirement: Output decoding
CLI stdout SHALL be collected as bytes and decoded as UTF-8 once, so multi-byte characters split across chunks are preserved.

#### Scenario: Non-ASCII output in small chunks
- **WHEN** the CLI writes non-ASCII JSON in chunks that split multi-byte characters
- **THEN** the parsed result contains no U+FFFD replacement characters

### Requirement: Shared snippet key
The package SHALL export `canonicalizeSnippet(code)` (CRLF → LF, leading/trailing blank space removed; also from the Node-free subpath `@glosharp/core/snippet`), `snippetKey(code, options?)` (SHA-256 hex of the canonical snippet, plus the result-affecting options `framework`, `project`, `region`, `noRestore`, `configFile`, `complog`, `complogProject` when any are set) and `hasGloSharpMarkers(code)`, so integrations key results identically on both sides of a lookup.

#### Scenario: Pipelines disagree on the trailing newline
- **WHEN** one side keys `"var x = 1;\n"` and the other looks up `"var x = 1;"` (or the CRLF form)
- **THEN** both produce the same key

#### Scenario: No options
- **WHEN** `snippetKey(code)` is called without options
- **THEN** it equals `sha256(canonicalizeSnippet(code))`, the GitBook artifact key

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

#### Scenario: Region with inline code
- **WHEN** `glosharp.process({ code, region: 'demo' })` is called
- **THEN** the CLI is spawned with `--stdin --region demo` (region extraction is text-based)

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

### Requirement: Complog option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `complog` property specifying a path to a `.complog` file, applied to all `process()` calls on the instance.

#### Scenario: Instance-level complog
- **WHEN** `createGloSharp({ complog: './build.complog' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--complog ./build.complog`

### Requirement: Complog option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `complog` property that overrides the instance-level `complog` for a single call.

#### Scenario: Per-call complog override
- **WHEN** `glosharp.process({ code: '...', complog: './other.complog' })` is called on an instance with a different `complog`
- **THEN** the CLI is spawned with `--complog ./other.complog`

#### Scenario: No complog
- **WHEN** `glosharp.process({ code: '...' })` is called on an instance without `complog`
- **THEN** the CLI is spawned without `--complog` (existing behavior unchanged)

### Requirement: ComplogProject option in GloSharpOptions
The `GloSharpOptions` interface SHALL accept an optional `complogProject` property specifying the project name to select from a multi-project complog.

#### Scenario: Instance-level complog project
- **WHEN** `createGloSharp({ complog: './build.complog', complogProject: 'MyLib' })` is called and `process()` is invoked
- **THEN** the CLI is spawned with `--complog ./build.complog --complog-project MyLib`

### Requirement: ComplogProject option in GloSharpProcessOptions
The `GloSharpProcessOptions` interface SHALL accept an optional `complogProject` property that overrides the instance-level value for a single call.

#### Scenario: Per-call complog project override
- **WHEN** `glosharp.process({ code: '...', complogProject: 'MyApp' })` is called
- **THEN** the CLI is spawned with `--complog-project MyApp`

### Requirement: Render method returns HTML
The instance SHALL expose a `render()` method that invokes the CLI's `render` command and returns its HTML output verbatim as a string. It SHALL accept every option `process()` accepts, plus `theme` (a built-in theme name) and `standalone` (wrap the fragment in a full HTML page). Option resolution SHALL match `process()`: per-call options override the instance options.

#### Scenario: Render inline code
- **WHEN** `glosharp.render({ code: 'var x = 42;\n//  ^?' })` is called
- **THEN** the CLI is spawned as `render --stdin` with the source on stdin, and the returned string is the CLI's stdout unchanged

#### Scenario: Theme and standalone forwarded
- **WHEN** `glosharp.render({ code: 'var x = 42;', theme: 'github-light', standalone: true })` is called
- **THEN** the CLI is spawned with `--theme github-light --standalone`

#### Scenario: Shared option surface
- **WHEN** `createGloSharp({ complog: './docs.glocontext' })` renders with `{ framework: 'net10.0' }`
- **THEN** the CLI is spawned with both `--framework net10.0` and `--complog ./docs.glocontext`

#### Scenario: Render failure surfaces stderr
- **WHEN** the CLI exits non-zero during a render
- **THEN** `render()` throws an error containing the exit code and stderr

### Requirement: TypeScript types include diagnostics extensions
`GloSharpResult` SHALL include `hiddenErrors: GloSharpError[]` (diagnostics in cut code), `GloSharpError` SHALL include optional `sourceLine` / `sourceCharacter` (0-based position in the original input), and `GloSharpMeta` SHALL include `warnings: string[]`. The bridge SHALL fill in `[]` for `hiddenErrors` and `meta.warnings` when an older CLI omits them. `GloSharpHover.docs` SHALL be optional (the JSON omits it when a symbol has no docs). The package SHALL export `unexpectedErrors(result)`, returning the errors that made `meta.compileSucceeded` false.

#### Scenario: Older CLI output
- **WHEN** the CLI output has no `hiddenErrors` or `meta.warnings`
- **THEN** the result has both as empty arrays

#### Scenario: Reporting a compile failure
- **WHEN** `meta.compileSucceeded` is false because of an unexpected error in visible code and one in cut code
- **THEN** `unexpectedErrors(result)` returns both, and neither expected (`@errors`) diagnostics nor warnings

### Requirement: CLI version compatibility check
`@glosharp/core` SHALL export `EXPECTED_CLI_VERSION`, the GloSharp.Cli version it was released with, generated from the single release version in `src/Directory.Build.props`. The first time an instance runs a CLI that was discovered (PATH, `~/.dotnet/tools`, local tool) or taken from `GLOSHARP_EXECUTABLE`, the bridge SHALL run `<cli> --version` in the background and, when the reported version's release line (`major.minor`, plus the prerelease identifier for prereleases) differs from `EXPECTED_CLI_VERSION`'s, emit one `GloSharpVersionWarning` per distinct mismatch naming both versions and the `dotnet tool update` command that installs the expected one. The check SHALL NOT delay or fail CLI runs, and SHALL be skipped for executables passed through the `executable` option, for `0.0.0-*` development builds, for output it cannot parse, and when `GLOSHARP_SKIP_VERSION_CHECK` is set to a value other than `0`/`false`.

#### Scenario: CLI from another release line
- **WHEN** `@glosharp/core` 0.1.0-alpha.2 finds a global `glosharp` that reports `0.2.0-alpha.1+abc`
- **THEN** a single warning names both versions and suggests `dotnet tool update --global GloSharp.Cli --version 0.1.0-alpha.2`, and every `process()` call still runs

#### Scenario: Same release line
- **WHEN** `@glosharp/core` 0.1.0-alpha.2 finds a CLI reporting `0.1.0-alpha.5`
- **THEN** no warning is emitted

#### Scenario: Explicit executable or development build
- **WHEN** the CLI is passed as the `executable` option, or reports `0.0.0-ci.42.1`
- **THEN** no warning is emitted
