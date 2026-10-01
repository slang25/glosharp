// A stand-in for the glosharp CLI, run as `node stub-cli.mjs <mode> <glosharp args...>`.
// The bridge's process-level behaviour (stdin, exit codes, timeouts,
// concurrency) is tested against real child processes rather than mocks.
//
// STUB_LOG_DIR (env): when set, each run writes <pid>.json with its argv and
// stdin, and keeps <pid>.running present while it runs (for concurrency checks).
import { writeFileSync, rmSync, readdirSync } from 'node:fs'
import { join } from 'node:path'

const [mode, ...args] = process.argv.slice(2)
const logDir = process.env.STUB_LOG_DIR

function readStdin() {
  return new Promise((resolve) => {
    const chunks = []
    process.stdin.on('data', (c) => chunks.push(c))
    process.stdin.on('end', () => resolve(Buffer.concat(chunks).toString('utf8')))
  })
}

function result(code) {
  return {
    code,
    original: code,
    lang: 'csharp',
    hovers: [],
    errors: [],
    completions: [],
    highlights: [],
    tags: [],
    hidden: [],
    meta: { targetFramework: 'net8.0', packages: [], compileSucceeded: true },
  }
}

const sleep = (ms) => new Promise((r) => setTimeout(r, ms))

if (args[0] === 'serve') {
  // Like a CLI from before `glosharp serve` existed.
  process.stderr.write("glosharp: error: unknown command 'serve'.\nRun 'glosharp --help' for the list of commands.\n")
  process.exit(2)
}

if (mode === 'exit-early') {
  // Exit without reading stdin: a large write from the parent hits EPIPE.
  process.stderr.write('Error: --region cannot be used with --stdin\n')
  process.exit(1)
}

if (mode === 'hang') {
  if (logDir) writeFileSync(join(logDir, `${process.pid}.pid`), String(process.pid))
  setInterval(() => {}, 1000)
} else {
  const stdin = await readStdin()
  let running
  if (logDir) {
    running = join(logDir, `${process.pid}.running`)
    writeFileSync(running, '')
    const concurrent = readdirSync(logDir).filter((f) => f.endsWith('.running')).length
    writeFileSync(join(logDir, `${process.pid}.json`), JSON.stringify({ args, stdin, concurrent }))
  }

  if (mode === 'slow') await sleep(150)

  if (mode === 'utf8') {
    // Non-ASCII JSON written in 7-byte slices so multi-byte characters are
    // split across chunks.
    const text = 'héllo wörld — ✓ 漢字 🎉 '.repeat(4000)
    const bytes = Buffer.from(JSON.stringify(result(text)), 'utf8')
    for (let i = 0; i < bytes.length; i += 7) {
      process.stdout.write(bytes.subarray(i, i + 7))
    }
  } else if (mode === 'legacy') {
    // An older CLI: no hiddenErrors, no meta.warnings.
    process.stdout.write(JSON.stringify(result(stdin)))
  } else if (mode === 'fail') {
    process.stderr.write('Error: compilation context could not be loaded\n')
    process.exitCode = 3
  } else {
    process.stdout.write(JSON.stringify({ ...result(stdin), hiddenErrors: [], args }))
  }

  if (running) rmSync(running, { force: true })
}
