// A stand-in for the bridge's `process()`, so Shiki/Markdown tests never run
// the real CLI. Behaviour is driven by the snippet text:
// - `BROKEN`  → compileSucceeded:false with an unexpected CS0103 (sourceLine 1)
// - `CRASH`   → rejects with a GloSharpCliError (kind 'exit')
// - `MISSING` → rejects with a GloSharpCliError (kind 'not-found')
// - `WARNME`  → meta.warnings has one entry
// Otherwise: a hover on the identifier after `var `, markers stripped.
import type { GloSharpProcessOptions, GloSharpResult } from '@glosharp/core'
// The real class (not via the mocked '@glosharp/core' entry), so instanceof
// checks in the code under test still hold.
import { GloSharpCliError } from '../../glosharp/dist/errors.js'

export const calls: GloSharpProcessOptions[] = []

export function fakeResult(opts: GloSharpProcessOptions): GloSharpResult {
  const code = (opts.code ?? '')
    .split('\n')
    .filter((line) => !/^\s*\/\/\s*(\^[?|]|@)/.test(line))
    .join('\n')
    .replace(/\n+$/, '')
  const firstLine = code.split('\n')[0] ?? ''
  const match = /var (\w+)/.exec(firstLine)
  const broken = code.includes('BROKEN')
  return {
    code,
    original: opts.code ?? '',
    lang: 'csharp',
    hovers: match
      ? [
          {
            line: 0,
            character: match.index + 4,
            length: match[1].length,
            text: `(local variable) int ${match[1]}`,
            parts: [
              { kind: 'punctuation', text: '(' },
              { kind: 'text', text: 'local variable' },
              { kind: 'punctuation', text: ')' },
              { kind: 'space', text: ' ' },
              { kind: 'keyword', text: 'int' },
              { kind: 'space', text: ' ' },
              { kind: 'localName', text: match[1] },
            ],
            symbolKind: 'Local',
            targetText: match[1],
          },
        ]
      : [],
    errors: broken
      ? [
          {
            line: 0,
            character: 0,
            length: 6,
            sourceLine: 1,
            sourceCharacter: 4,
            code: 'CS0103',
            message: "The name 'BROKEN' does not exist in the current context",
            severity: 'error',
            expected: false,
          },
        ]
      : [],
    hiddenErrors: [],
    completions: [],
    highlights: [],
    tags: [],
    hidden: [],
    meta: {
      targetFramework: opts.framework ?? 'net8.0',
      packages: [],
      compileSucceeded: !broken,
      warnings: code.includes('WARNME') ? ['caret points past the end of line 1'] : [],
    },
  }
}

export async function fakeProcess(opts: GloSharpProcessOptions): Promise<GloSharpResult> {
  calls.push(opts)
  if (opts.code?.includes('CRASH')) {
    throw new GloSharpCliError('glosharp exited with code 1:\nError: boom', { kind: 'exit', exitCode: 1, stderr: 'Error: boom' })
  }
  if (opts.code?.includes('MISSING')) {
    throw new GloSharpCliError('glosharp CLI not found.\nInstall the CLI with one of: …', { kind: 'not-found' })
  }
  return fakeResult(opts)
}
