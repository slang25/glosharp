export interface GloSharpDisplayPart {
  kind: string
  text: string
}

export interface GloSharpDocParam {
  name: string
  text: string
}

export interface GloSharpDocException {
  type: string
  text: string
}

export interface GloSharpDocComment {
  summary?: string | null
  params?: GloSharpDocParam[]
  returns?: string | null
  remarks?: string | null
  examples?: string[]
  exceptions?: GloSharpDocException[]
}

export interface GloSharpTypeAnnotation {
  name: string
  expansion: string
}

export interface GloSharpHover {
  line: number
  character: number
  length: number
  text: string
  parts: GloSharpDisplayPart[]
  /** XML documentation. Omitted from the JSON when the symbol has none. */
  docs?: GloSharpDocComment | null
  symbolKind: string
  targetText: string
  overloadCount?: number
  typeAnnotations?: GloSharpTypeAnnotation[] | null
  /** True for hovers requested with a `^?` query; these are shown without hovering. */
  persistent?: boolean
}

/**
 * Diagnostic code. Roslyn codes look like `CS0103`; diagnostics Glo# raises
 * itself use the `GS` prefix:
 * - `GS0001` invalid `@langVersion`
 * - `GS0002` invalid `@nullable`
 * - `GS0003` an `// @errors:` expectation that matched no diagnostic
 *
 * See `design/data-format.md` for the full list.
 */
export type GloSharpDiagnosticCode = string

export interface GloSharpError {
  /** 0-based line in the processed `code`. */
  line: number
  character: number
  length: number
  endLine?: number
  endCharacter?: number
  /**
   * 0-based line in the ORIGINAL input given to the processor (before marker,
   * cut, directive and region stripping). Use it to report `file:line`.
   * Absent when produced by an older CLI.
   */
  sourceLine?: number
  /** 0-based character in the original input. See `sourceLine`. */
  sourceCharacter?: number
  code: GloSharpDiagnosticCode
  message: string
  severity: 'error' | 'warning' | 'info' | 'hidden'
  /** True when an `// @errors:` directive declared this diagnostic. */
  expected: boolean
}

export interface GloSharpMeta {
  targetFramework: string
  packages: { name: string; version: string }[]
  /**
   * False when compilation produced an unexpected error — including errors in
   * hidden (cut) code and unmatched `@errors:` expectations — unless
   * suppressed with `@noErrors`.
   */
  compileSucceeded: boolean
  sdk?: string | null
  langVersion?: string | null
  nullable?: string | null
  complog?: string | null
  /**
   * Non-fatal problems worth surfacing to the author: package restore
   * failures, a `^?` / `^|` caret pointing past the end of its line, etc.
   * Always present (the bridge fills in `[]` for older CLIs).
   */
  warnings: string[]
}

export type GloSharpCompletionKind = string

export interface GloSharpCompletionItem {
  label: string
  kind: GloSharpCompletionKind
  detail: string | null
}

export interface GloSharpCompletion {
  line: number
  character: number
  items: GloSharpCompletionItem[]
}

export interface GloSharpHighlight {
  line: number
  character: number
  length: number
  kind: 'highlight' | 'focus' | 'add' | 'remove'
}

export interface GloSharpTag {
  name: 'log' | 'warn' | 'error' | 'annotate'
  text: string
  line: number
}

export interface GloSharpResult {
  code: string
  original: string
  lang: string
  hovers: GloSharpHover[]
  errors: GloSharpError[]
  /**
   * Diagnostics located in hidden (cut) code. Renderers cannot place them, but
   * they make `meta.compileSucceeded` false. Always present (the bridge fills
   * in `[]` for older CLIs).
   */
  hiddenErrors: GloSharpError[]
  completions: GloSharpCompletion[]
  highlights: GloSharpHighlight[]
  tags: GloSharpTag[]
  /** Reserved; currently always empty. */
  hidden: unknown[]
  meta: GloSharpMeta
}

/** How to start the CLI: a path or command name, or a command plus leading arguments. */
export type GloSharpExecutable = string | readonly string[]

export interface GloSharpOptions {
  /**
   * The glosharp CLI to run. A path or command name, a `.dll` (run with
   * `dotnet`), or an argv prefix such as `['dotnet', 'path/to/GloSharp.Cli.dll']`.
   * Defaults to `$GLOSHARP_EXECUTABLE`, then `glosharp` on PATH, then
   * `~/.dotnet/tools/glosharp`, then a local tool (`dotnet glosharp`).
   */
  executable?: GloSharpExecutable
  framework?: string
  cacheDir?: string
  configFile?: string
  complog?: string
  complogProject?: string
  /**
   * Maximum CLI processes this instance runs at once. All instances also share
   * a process-wide limit (see `configureGloSharp`).
   */
  concurrency?: number
  /**
   * Kill a CLI run that takes longer than this, in milliseconds. `0` disables
   * the timeout. Defaults to `$GLOSHARP_TIMEOUT_MS` or 180000 (3 minutes).
   */
  timeoutMs?: number
  /** Maximum number of results kept in the in-memory cache. Defaults to 1000. */
  cacheSize?: number
  /**
   * Number of long-running `glosharp serve` workers to run snippets on,
   * shared by every instance using the same CLI. `0` runs one CLI process per
   * snippet instead (much slower). Defaults to `$GLOSHARP_WORKERS` or
   * `min(2, cpus - 1)` (see `configureGloSharp`). CLIs without `serve` fall
   * back to one process per snippet automatically.
   */
  workers?: number
}

export interface GloSharpProcessOptions {
  code?: string
  file?: string
  framework?: string
  project?: string
  /** Extract a `#region` by name (works with both `code` and `file`). */
  region?: string
  noRestore?: boolean
  cacheDir?: string
  configFile?: string
  complog?: string
  complogProject?: string
  /** Per-call timeout override, in milliseconds. */
  timeoutMs?: number
  /** Abort the CLI run (the process is killed). */
  signal?: AbortSignal
}

export interface GloSharpRenderOptions extends GloSharpProcessOptions {
  /** Built-in theme name. Defaults to the CLI's default (`github-dark`). */
  theme?: string
  /** Wrap the fragment in a full HTML page. */
  standalone?: boolean
}
