export interface TwohashDisplayPart {
  kind: string
  text: string
}

export interface TwohashHover {
  line: number
  character: number
  length: number
  text: string
  parts: TwohashDisplayPart[]
  docs: string | null
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
}

export interface TwohashResult {
  code: string
  original: string
  lang: string
  hovers: TwohashHover[]
  errors: TwohashError[]
  completions: unknown[]
  highlights: unknown[]
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
  noRestore?: boolean
}
