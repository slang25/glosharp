## ADDED Requirements

### Requirement: Canonical snippet form
The package SHALL export `canonicalizeSnippet(code)` producing the form that is hashed to key an artifact. It SHALL normalise `\r\n` and `\r` to `\n`, remove leading blank lines, and remove trailing whitespace at the end of the input. It SHALL NOT alter anything else — in particular it SHALL preserve leading indentation, interior blank lines, and trailing whitespace on interior lines. It SHALL be idempotent.

#### Scenario: Line endings normalised
- **WHEN** `canonicalizeSnippet("a\r\nb\rc")` is called
- **THEN** it returns `"a\nb\nc"`

#### Scenario: Surrounding blank space dropped
- **WHEN** `canonicalizeSnippet("\n\nvar x = 1;\n\n  \n")` is called
- **THEN** it returns `"var x = 1;"`

#### Scenario: Indentation preserved
- **WHEN** `canonicalizeSnippet("\n    var x = 1;")` is called
- **THEN** it returns `"    var x = 1;"`

#### Scenario: Interior whitespace preserved
- **WHEN** `canonicalizeSnippet("a   \n\nb")` is called
- **THEN** it returns `"a   \n\nb"` (a raw string literal may depend on it)

#### Scenario: Idempotent
- **WHEN** `canonicalizeSnippet` is applied twice to any input
- **THEN** the second application returns its input unchanged

### Requirement: Artifact key
The package SHALL export `snippetKey(code)` returning the lowercase hex SHA-256 of the UTF-8 bytes of `canonicalizeSnippet(code)`.

#### Scenario: Key is the SHA-256 of the canonical form
- **WHEN** `snippetKey("var x = 42;")` is called
- **THEN** it equals `sha256("var x = 42;")` in lowercase hex

#### Scenario: Key is stable across incidental differences
- **WHEN** `snippetKey("var x = 42;")` and `snippetKey("\r\nvar x = 42;\r\n\r\n")` are compared
- **THEN** they are equal

### Requirement: Fence discovery in Markdown
The package SHALL export `findFences(markdown, lang?)` returning each fenced code block, with its info-string language, its parsed attributes, its body with the opening fence's indentation removed, and the 1-based line number of the opening fence. Fence handling SHALL follow CommonMark for what decides a body's extent: an opening fence of three or more backticks or tildes indented at most three spaces; a closing fence of the same character and at least the same length; a backtick info string containing a backtick disqualifies the fence; an unterminated fence runs to end of input. When `lang` is given, matching SHALL be case-insensitive and only matching blocks SHALL be returned. Blocks inside another fence's body SHALL NOT be returned.

#### Scenario: Fence found with position
- **WHEN** `findFences("# Title\n\n```glosharp\nvar x = 42;\n```\n", "glosharp")` is called
- **THEN** one block is returned with `code` `"var x = 42;"` and `line` 3

#### Scenario: Other languages excluded
- **WHEN** a document contains a `csharp` fence and a `glosharp` fence and `findFences(doc, "glosharp")` is called
- **THEN** only the `glosharp` fence is returned

#### Scenario: Language matched case-insensitively
- **WHEN** a document contains a ` ```GloSharp ` fence and `findFences(doc, "glosharp")` is called
- **THEN** the fence is returned

#### Scenario: Indentation stripped
- **WHEN** a `glosharp` fence is indented two spaces inside a list item
- **THEN** the returned `code` has two leading spaces removed from each body line

#### Scenario: Longer outer fence contains shorter fences
- **WHEN** a four-backtick `glosharp` fence contains a three-backtick fence in its body
- **THEN** the inner fence is part of the returned `code` and is not treated as a terminator

#### Scenario: Nested fence not discovered
- **WHEN** a four-backtick `md` fence contains a three-backtick `glosharp` fence
- **THEN** `findFences(doc, "glosharp")` returns nothing

#### Scenario: Unterminated fence
- **WHEN** a document ends without closing a `glosharp` fence
- **THEN** the body runs to the end of the document

### Requirement: Fence attributes
The package SHALL export `parseFenceAttributes(tail)` parsing an info string's attribute tail into a record. It SHALL accept `key="value"`, `key='value'`, `key=value`, and bare `key` (which SHALL map to `true`). A repeated key SHALL take its last value.

#### Scenario: All attribute forms
- **WHEN** `parseFenceAttributes("a=\"one two\" b='three' c=four d")` is called
- **THEN** it returns `{ a: "one two", b: "three", c: "four", d: true }`

#### Scenario: Empty tail
- **WHEN** `parseFenceAttributes("")` is called
- **THEN** it returns an empty record

### Requirement: Markdown file collection
The package SHALL export `collectMarkdownFiles(paths, options?)` expanding a mix of files and directories into a sorted, deduplicated list. Directories SHALL be walked recursively, matching `.md` and `.mdx` by default and skipping `node_modules`, `.git`, and `.gitbook`. A path given explicitly SHALL be included regardless of its extension.

#### Scenario: Directory walked
- **WHEN** a directory contains `b.md`, `a/c.mdx`, `a/notes.txt`, and `node_modules/skip.md`
- **THEN** only `a/c.mdx` and `b.md` are returned, sorted

#### Scenario: Explicit file honoured
- **WHEN** `collectMarkdownFiles(["page.markdown"])` is called
- **THEN** `page.markdown` is returned

### Requirement: Snippet collection with occurrence tracking
The package SHALL export `collectSnippets({ files, root, fence })` returning the distinct snippets keyed by `snippetKey`, each with every occurrence (`file` relative to `root` using `/` separators, and `line`), sorted by key. Two occurrences of the same key that carry different `framework` attributes SHALL raise an error naming both locations and both values.

#### Scenario: Occurrences merged under one key
- **WHEN** two files contain snippets differing only in line endings and surrounding blank lines
- **THEN** one snippet is returned with both occurrences

#### Scenario: POSIX separators
- **WHEN** a snippet is found in `deep/nested/c.md`
- **THEN** its occurrence path is `deep/nested/c.md` on every platform

#### Scenario: Conflicting framework rejected
- **WHEN** two byte-identical snippets declare `framework="net9.0"` and `framework="net10.0"`
- **THEN** collection fails with an error naming both files, both lines, and both frameworks

### Requirement: Artifact publication layout
`buildArtifacts` SHALL write one rendered fragment per (theme, snippet) to `<outDir>/<theme>/<key>.html`, and an `index.json` describing what was published. Themes SHALL default to `github-dark` and `github-light`.

#### Scenario: One fragment per theme
- **WHEN** a build with themes `github-dark` and `github-light` publishes one snippet
- **THEN** `github-dark/<key>.html` and `github-light/<key>.html` are written

#### Scenario: Fence body rendered verbatim
- **WHEN** a snippet body contains interior blank lines and indentation
- **THEN** the renderer receives that body unchanged

### Requirement: Deterministic index
`index.json` SHALL contain a schema `version`, the `fence` claimed, the sorted `themes`, and a `snippets` map from key to `{ framework?, occurrences }`. Keys, themes, and occurrences SHALL be sorted, and `framework` SHALL be omitted when absent, so a rebuild from unchanged input produces byte-identical output.

#### Scenario: Rebuild is byte-identical
- **WHEN** a build runs twice over unchanged input
- **THEN** the second run reports nothing changed and `index.json` is byte-identical

#### Scenario: Sorted and minimal
- **WHEN** an index is built from unsorted snippets and occurrences
- **THEN** snippet keys, themes, and occurrences are sorted and snippets without a framework have no `framework` key

### Requirement: Reuse, drift detection, and pruning
`buildArtifacts` SHALL classify every artifact as changed or unchanged by comparing rendered output with what is on disk. With `check`, it SHALL write nothing and report what would change. With `skipExisting`, it SHALL reuse an artifact present on disk without rendering it. It SHALL report every `.html` file in *any* directory under `outDir` that this build does not claim — whether because no snippet has that key or because the build no longer renders that theme — and with `prune` SHALL delete them. A render failure SHALL be collected per (key, theme) rather than aborting the build.

#### Scenario: Unchanged artifacts reused
- **WHEN** a build runs twice over unchanged input
- **THEN** the second run reports every artifact as unchanged

#### Scenario: Check writes nothing
- **WHEN** a build runs with `check` against an empty output directory
- **THEN** the drift is reported and no files are created

#### Scenario: Stale artifact is drift
- **WHEN** an artifact on disk no longer matches what the renderer produces
- **THEN** a `check` build reports it as changed

#### Scenario: Existing artifacts skipped
- **WHEN** a build runs with `skipExisting` and every artifact is present
- **THEN** the renderer is not invoked

#### Scenario: Orphans reported and pruned
- **WHEN** the output directory contains an artifact whose key no snippet claims
- **THEN** it is reported as orphaned, and deleted when `prune` is set

#### Scenario: Dropped theme is orphaned
- **WHEN** a build that previously rendered two themes is narrowed to one
- **THEN** the dropped theme's artifacts are reported as orphaned

#### Scenario: Failures collected
- **WHEN** one snippet fails to render
- **THEN** its failure is reported with key, theme, and message, and every other snippet is still published

### Requirement: glosharp-gitbook CLI
The package SHALL provide a `glosharp-gitbook` executable with `build <paths...> --out <dir>`, `dev <paths...>`, and `scan <paths...>`. `build` SHALL accept `--fence`, repeatable `--theme`, `--concurrency`, `--check`, `--skip-existing`, `--prune`, and the compilation options `--framework`, `--project`, `--complog`, `--config`, `--cache-dir`, `--executable` forwarded to the `glosharp` CLI. `scan` SHALL list the discovered snippets, as JSON with `--json`. An unknown option or a value option with no value SHALL be an error, not silently ignored. `build` SHALL exit non-zero when any render failed, or when `--check` found drift. An `--executable` value that names a path which does not exist SHALL be rejected before any render is attempted.

#### Scenario: Missing executable path rejected early
- **WHEN** `--executable ./not/built` is passed and that file does not exist
- **THEN** the CLI reports it, suggests building the CLI, and exits non-zero without rendering

#### Scenario: Repeated theme flags collected
- **WHEN** `--theme github-dark --theme github-light` is passed
- **THEN** both themes are used, in that order

#### Scenario: Unknown option rejected
- **WHEN** `--nope x` is passed
- **THEN** the CLI reports `Unknown option: --nope` and exits non-zero

#### Scenario: Missing value rejected
- **WHEN** `--out` is passed with no value
- **THEN** the CLI reports that `--out` requires a value and exits non-zero

#### Scenario: Check gates a pull request
- **WHEN** `build --check` finds an artifact that would change
- **THEN** each drifting path is listed and the exit code is 1

### Requirement: Local preview command
The CLI SHALL provide `dev <paths...>`, serving the real webframe shell, the built artifacts, and a host page that implements GitBook's webframe contract — so a snippet can be previewed exactly as the block will show it without a GitBook account. Requesting the host page SHALL re-scan and rebuild, rendering only snippets not already published, so the loop is "edit Markdown, reload". It SHALL accept `--port`, `--frame-theme`, `--out` (defaulting to a stable per-project temp directory), `--fresh`, and `--no-build`, and it SHALL serve files only from inside the artifacts directory.

#### Scenario: The host listens before the frames exist
- **WHEN** the preview page is served
- **THEN** its host script runs before the first frame is parsed, so no readiness announcement is lost

#### Scenario: Preview renders and serves
- **WHEN** `glosharp-gitbook dev docs` is run and the host page is requested
- **THEN** the snippets are rendered, and the page contains one webframe per snippet pointing at the shell

#### Scenario: Reload picks up an edit
- **WHEN** a snippet is edited and the host page is requested again
- **THEN** only the changed snippet is re-rendered and the page reflects the edit

#### Scenario: Artifacts are served, traversal is not
- **WHEN** a request asks for a path that escapes the artifacts directory
- **THEN** the server refuses it

### Requirement: Reusable GitHub Action
The package SHALL ship a composite GitHub Action that installs the `glosharp` dotnet tool and runs `glosharp-gitbook build`, with inputs for the scan paths, output directory, fence, themes, compilation options, concurrency, `check`, `prune`, package and tool versions, and whether to install the .NET SDK.

#### Scenario: Optional flags omitted when unset
- **WHEN** the action runs with no `framework`, `project`, `complog`, or `config` input
- **THEN** none of those flags are passed and the step succeeds
