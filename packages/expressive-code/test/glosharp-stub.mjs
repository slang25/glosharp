#!/usr/bin/env node
// Fake `glosharp` executable for engine-level tests: emits a canned
// GloSharpResult chosen by the incoming source, so tests can render blocks
// through the real Expressive Code engine without the .NET CLI.
let input = ''
process.stdin.setEncoding('utf-8')
for await (const chunk of process.stdin) input += chunk

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
if (input.includes('Console.')) {
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
