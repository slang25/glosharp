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
  docs: GloSharpDocComment | null
  symbolKind: string
  targetText: string
  overloadCount?: number
  typeAnnotations?: GloSharpTypeAnnotation[] | null
  persistent?: boolean
}

export interface GloSharpError {
  line: number
  character: number
  length: number
  endLine?: number
  endCharacter?: number
  code: string
  message: string
  severity: 'error' | 'warning' | 'info' | 'hidden'
  expected: boolean
}

export interface GloSharpMeta {
  targetFramework: string
  packages: { name: string; version: string }[]
  compileSucceeded: boolean
  sdk?: string | null
  langVersion?: string | null
  nullable?: string | null
  complog?: string | null
}

export interface GloSharpCompletionItem {
  label: string
  kind: string
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
  completions: GloSharpCompletion[]
  highlights: GloSharpHighlight[]
  tags: GloSharpTag[]
  hidden: unknown[]
  meta: GloSharpMeta
}

export interface GloSharpOptions {
  executable?: string
  framework?: string
  cacheDir?: string
  configFile?: string
  complog?: string
  complogProject?: string
}

export interface GloSharpProcessOptions {
  code?: string
  file?: string
  framework?: string
  project?: string
  region?: string
  noRestore?: boolean
  cacheDir?: string
  configFile?: string
  complog?: string
  complogProject?: string
}
