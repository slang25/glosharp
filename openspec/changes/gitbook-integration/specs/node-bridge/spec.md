## ADDED Requirements

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

## MODIFIED Requirements

### Requirement: Factory function creates glosharp instance
The package SHALL export a `createGloSharp()` function that accepts configuration options and returns a glosharp instance with `process()` and `render()` methods.

#### Scenario: Create default instance
- **WHEN** `createGloSharp()` is called with no options
- **THEN** a glosharp instance is returned that auto-detects the `glosharp` CLI on PATH

#### Scenario: Custom executable path
- **WHEN** `createGloSharp({ executable: '/path/to/glosharp' })` is called
- **THEN** the instance uses the specified path to spawn the CLI

### Requirement: Cache results during build
The instance SHALL cache results by source code hash to avoid re-processing identical snippets within a single build. `render()` SHALL use a separate cache keyed by the source hash *and* the rendering arguments, since the same source yields different HTML per theme, and SHALL NOT share entries with `process()`.

#### Scenario: Duplicate snippet skips CLI
- **WHEN** `process()` is called twice with identical source code
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
