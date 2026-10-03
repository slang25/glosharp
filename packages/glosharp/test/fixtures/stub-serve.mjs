// A stand-in for a glosharp CLI with `serve`, run as
// `node stub-serve.mjs <mode> <glosharp args...>`.
//
// mode (how `serve` starts): ok | protocol2 | garbage | die | mute | naive
// Per request, directives in the code steer the response:
//   @delay:N   answer after N ms
//   @hang      never answer
//   @crash     exit(3) after writing to stderr (after @delay, if any)
//   @fail      answer with an error like `glosharp process` failing
//   @usage     answer with a usage error
//   @noise     write a non-protocol line to stdout instead of answering
//
// Without `serve` it behaves like a one-shot `process`/`render` (result.via = 'cli').
//
// STUB_LOG_DIR (env): each serve worker writes <pid>.worker at start; each
// one-shot run writes <pid>.cli.
import { writeFileSync, appendFileSync } from 'node:fs'
import { join } from 'node:path'
import { createInterface } from 'node:readline'

const [mode, ...args] = process.argv.slice(2)
const logDir = process.env.STUB_LOG_DIR
const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

function result(code, extra) {
  return {
    code,
    original: code,
    lang: 'csharp',
    hovers: [],
    errors: [],
    hiddenErrors: [],
    completions: [],
    highlights: [],
    tags: [],
    hidden: [],
    meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true, warnings: [] },
    ...extra,
  }
}

// 'naive': a stub/wrapper that treats every command as a one-shot run, so
// `serve` just waits for the end of stdin.
if (args[0] !== 'serve' || mode === 'naive') {
  const chunks = []
  for await (const chunk of process.stdin) chunks.push(chunk)
  const stdin = Buffer.concat(chunks).toString('utf8')
  if (logDir) writeFileSync(join(logDir, `${process.pid}.cli`), JSON.stringify({ args }))
  if (args[0] === 'render') process.stdout.write(`<pre data-via="cli">${stdin}</pre>`)
  else process.stdout.write(JSON.stringify(result(stdin, { via: 'cli', args })))
  process.exit(0)
}

if (mode === 'die') {
  process.stderr.write('Unhandled exception. System.TypeLoadException: boom\n')
  process.exit(134)
}

if (args.includes('--help')) {
  // The bridge's feature probe.
  process.stdout.write(mode === 'garbage' ? 'Welcome to glosharp!\n' : 'Usage: glosharp serve [options]\n')
  process.exit(0)
}

if (logDir) writeFileSync(join(logDir, `${process.pid}.worker`), '')
if (mode === 'garbage') process.stdout.write('Welcome to glosharp!\n')
else if (mode === 'protocol2') process.stdout.write(JSON.stringify({ type: 'ready', protocol: 2, version: '9.0.0' }) + '\n')
else if (mode !== 'mute') process.stdout.write(JSON.stringify({ type: 'ready', protocol: 1, version: 'stub' }) + '\n')

const send = (message) => process.stdout.write(JSON.stringify(message) + '\n')
let inflight = 0
let ended = false
const maybeExit = () => {
  if (ended && inflight === 0) process.exit(0)
}

const lines = createInterface({ input: process.stdin })
lines.on('line', async (line) => {
  const request = JSON.parse(line)
  if (logDir) appendFileSync(join(logDir, `${process.pid}.requests`), line + '\n')
  const code = request.code ?? ''
  inflight++

  const delay = /@delay:(\d+)/.exec(code)
  if (delay) await sleep(Number(delay[1]))

  if (code.includes('@hang')) return
  if (code.includes('@crash')) {
    process.stderr.write('Fatal error. Internal CLR error.\n')
    process.exit(3)
  }
  if (code.includes('@noise')) {
    process.stdout.write('debug: something printed to stdout\n')
  } else if (code.includes('@fail')) {
    send({
      id: request.id,
      ok: false,
      error: {
        kind: 'failure',
        message: 'compilation context could not be loaded',
        exitCode: 1,
        stderr: 'glosharp process: error: compilation context could not be loaded\n',
      },
    })
  } else if (code.includes('@usage')) {
    send({
      id: request.id,
      ok: false,
      error: { kind: 'usage', message: "unknown theme 'x'", exitCode: 2, stderr: "glosharp render: error: unknown theme 'x'\n" },
    })
  } else if (request.command === 'render') {
    send({ id: request.id, ok: true, result: `<pre data-via="serve">${code}</pre>` })
  } else {
    send({ id: request.id, ok: true, result: result(code, { via: 'serve', pid: process.pid, options: request.options }) })
  }
  inflight--
  maybeExit()
})
lines.on('close', () => {
  ended = true
  maybeExit()
})
