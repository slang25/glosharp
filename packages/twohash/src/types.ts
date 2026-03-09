export interface TwohashDisplayPart {
  kind: string
  text: string
}

export interface TwohashDocParam {
  name: string
  text: string
}

export interface TwohashDocException {
  type: string
  text: string
}

export interface TwohashDocComment {
  summary?: string | null
  params?: TwohashDocParam[]
  returns?: string | null
  remarks?: string | null
  examples?: string[]
  exceptions?: TwohashDocException[]
}

export interface TwohashHover {
  line: number
  character: number
  length: number
  text: string
  parts: TwohashDisplayPart[]
  docs: TwohashDocComment | null
  symbolKind: string
  targetText: string
  overloadCount?: number
}

export interface TwohashError {
  line: number
  character: number
  length: number
  code: string
  message: string
  severity: 'error' | 'warning' | 'info' | 'hidden'
  expected: boolean
}

export interface TwohashMeta {
  targetFramework: string
  packages: { name: string; version: string }[]
  compileSucceeded: boolean
  sdk?: string | null
}

export interface TwohashCompletionItem {
  label: string
  kind: string
  detail: string | null
}

export interface TwohashCompletion {
  line: number
  character: number
  items: TwohashCompletionItem[]
}

export interface TwohashHighlight {
  line: number
  character: number
  length: number
  kind: 'highlight' | 'focus' | 'add' | 'remove'
}

export interface TwohashResult {
  code: string
  original: string
  lang: string
  hovers: TwohashHover[]
  errors: TwohashError[]
  completions: TwohashCompletion[]
  highlights: TwohashHighlight[]
  hidden: unknown[]
  meta: TwohashMeta
}

export interface TwohashOptions {
  executable?: string
  framework?: string
}

export interface TwohashProcessOptions {
  code?: string
  file?: string
  framework?: string
  project?: string
  region?: string
  noRestore?: boolean
}
