#!/usr/bin/env node
// Fake `glosharp` executable for engine-level tests: emits a canned
// GloSharpResult chosen by the incoming source, so tests can render blocks
// through the real Expressive Code engine without the .NET CLI.
//
// Test hooks (environment variables):
//   GLOSHARP_STUB_MAP   path to a JSON object { "<substring of input>": <response> }.
//                       The first key contained in the input wins. A response is
//                       either a (partial) GloSharpResult, merged over an empty
//                       result whose `code` is the input, or
//                       { "exit": <code>, "stderr": "<text>" } to simulate a CLI failure.
//   GLOSHARP_STUB_ARGS  path of a file; each invocation appends its argv as a JSON line.
import { appendFileSync, readFileSync } from 'node:fs'

let input = ''
process.stdin.setEncoding('utf-8')
for await (const chunk of process.stdin) input += chunk

if (process.env.GLOSHARP_STUB_ARGS) {
  appendFileSync(process.env.GLOSHARP_STUB_ARGS, JSON.stringify(process.argv.slice(2)) + '\n')
}

const base = {
  original: input,
  lang: 'csharp',
  hovers: [],
  errors: [],
  completions: [],
  highlights: [],
  tags: [],
  hidden: [],
  meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true },
}

let result
const mapFile = process.env.GLOSHARP_STUB_MAP
const map = mapFile ? JSON.parse(readFileSync(mapFile, 'utf-8')) : {}
const key = Object.keys(map).find(k => input.includes(k))

if (key !== undefined) {
  const response = map[key]
  if (typeof response.exit === 'number') {
    process.stderr.write(response.stderr ?? '')
    process.exit(response.exit)
  }
  result = { ...base, code: input, ...response, meta: { ...base.meta, ...(response.meta ?? {}) } }
} else if (input.includes('Console.')) {
  result = {
    ...base,
    code: 'Console.\n',
    completions: [{
      line: 0,
      character: 8,
      items: [
        { label: 'WriteLine', kind: 'Method', detail: 'void Console.WriteLine(string?)' },
        { label: 'ReadLine', kind: 'Method', detail: 'string? Console.ReadLine()' },
      ],
    }],
  }
} else if (input.includes('"hello"')) {
  result = {
    ...base,
    code: 'int total = "hello" +\n    " world" +\n    "!";\n',
    errors: [{
      line: 0,
      character: 12,
      length: 7,
      endLine: 2,
      endCharacter: 8,
      code: 'CS0029',
      message: "Cannot implicitly convert type 'string' to 'int'",
      severity: 'error',
      expected: false,
    }],
    meta: { ...base.meta, compileSucceeded: false },
  }
} else {
  result = { ...base, code: input }
}

process.stdout.write(JSON.stringify(result))
