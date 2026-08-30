// Regenerates the committed fixtures from the curated samples by running the
// GloSharp CLI: `process` output as GloSharpResult JSON (fixtures/*.json) and
// `render` output as self-contained HTML per theme (fixtures/html/*.html).
// With --check, regenerates to memory and diffs against the committed fixtures
// instead of writing, exiting non-zero on divergence.
//
// The HTML fixtures exist so the standalone renderer gets the same browser-level
// coverage as the Shiki and Expressive Code paths — it is the only path whose
// markup and CSS ship together, and the only one a GitBook reader ever sees.
//
// Requires the .NET SDK. The rendering gallery and Playwright suite only read
// the committed fixtures and never invoke this script.
import { execFileSync, execFile } from 'node:child_process'
import { promisify } from 'node:util'
import { mkdirSync, readdirSync, readFileSync, writeFileSync, existsSync } from 'node:fs'
import { join, resolve, basename } from 'node:path'

const execFileAsync = promisify(execFile)

const ROOT = resolve(import.meta.dirname!, '../../..')
const CLI_PROJECT = join(ROOT, 'src/GloSharp.Cli/GloSharp.Cli.csproj')
const SAMPLES_DIR = join(ROOT, 'samples')
const FIXTURES_DIR = resolve(import.meta.dirname!, '../fixtures')
const HTML_FIXTURES_DIR = join(FIXTURES_DIR, 'html')

// Pinned so fixtures do not depend on which SDKs the generating machine has installed.
const FRAMEWORK = 'net8.0'
const CONCURRENCY = 4
const THEMES = ['github-dark', 'github-light'] as const
const FRAGMENT_MARKER = '<div class="glosharp-code"'

interface Fixture {
  sample: string
  source: string
  result: unknown
}

// Replace machine-specific absolute paths so fixtures only change when the
// semantic output changes. (Current CLI output contains none; this is a guard.)
function normalize(value: unknown): unknown {
  if (typeof value === 'string') return value.split(ROOT).join('<repo>')
  if (Array.isArray(value)) return value.map(normalize)
  if (value && typeof value === 'object') {
    return Object.fromEntries(Object.entries(value).map(([k, v]) => [k, normalize(v)]))
  }
  return value
}

async function processSample(file: string): Promise<Fixture> {
  const { stdout } = await execFileAsync(
    'dotnet',
    ['run', '--no-build', '--project', CLI_PROJECT, '--', 'process', join(SAMPLES_DIR, file), '--framework', FRAMEWORK],
    { encoding: 'utf-8', timeout: 60000, maxBuffer: 32 * 1024 * 1024 },
  )
  // Tolerate any non-JSON noise before the payload
  const jsonStart = stdout.indexOf('{')
  const result = normalize(JSON.parse(stdout.slice(jsonStart)))
  return {
    sample: file,
    source: readFileSync(join(SAMPLES_DIR, file), 'utf-8'),
    result,
  }
}

async function renderSample(file: string, theme: string): Promise<string> {
  const { stdout } = await execFileAsync(
    'dotnet',
    ['run', '--no-build', '--project', CLI_PROJECT, '--', 'render', join(SAMPLES_DIR, file), '--framework', FRAMEWORK, '--theme', theme],
    { encoding: 'utf-8', timeout: 60000, maxBuffer: 32 * 1024 * 1024 },
  )
  // indexOf returning -1 would slice(-1) into a one-character fixture and
  // overwrite every committed file with it.
  const start = stdout.indexOf(FRAGMENT_MARKER)
  if (start < 0) {
    throw new Error(
      `render produced no ${FRAGMENT_MARKER} fragment for ${file} (${theme}). Output was:\n` +
        stdout.slice(0, 500),
    )
  }
  return stdout.slice(start)
}

function fixtureFileName(sample: string): string {
  return `${basename(sample, '.cs')}.json`
}

function htmlFixtureName(sample: string, theme: string): string {
  return `${basename(sample, '.cs')}.${theme}.html`
}

function serialize(fixture: Fixture): string {
  return JSON.stringify(fixture, null, 2) + '\n'
}

async function mapWithConcurrency<T, R>(items: T[], limit: number, fn: (item: T) => Promise<R>): Promise<R[]> {
  const results: R[] = new Array(items.length)
  let next = 0
  async function worker() {
    while (next < items.length) {
      const i = next++
      results[i] = await fn(items[i])
    }
  }
  await Promise.all(Array.from({ length: Math.min(limit, items.length) }, worker))
  return results
}

async function main() {
  const checkMode = process.argv.includes('--check')

  const samples = readdirSync(SAMPLES_DIR).filter(f => f.endsWith('.cs')).sort()
  if (samples.length === 0) {
    console.error(`No samples found in ${SAMPLES_DIR}`)
    process.exit(1)
  }

  console.log(`Building GloSharp CLI...`)
  execFileSync('dotnet', ['build', CLI_PROJECT, '-v', 'q'], { encoding: 'utf-8', timeout: 180000 })

  console.log(`Processing ${samples.length} samples (framework: ${FRAMEWORK})...`)
  const fixtures = await mapWithConcurrency(samples, CONCURRENCY, processSample)

  console.log(`Rendering ${samples.length * THEMES.length} HTML fixtures...`)
  const htmlJobs = samples.flatMap(sample => THEMES.map(theme => ({ sample, theme })))
  const rendered = await mapWithConcurrency(htmlJobs, CONCURRENCY, async ({ sample, theme }) => ({
    name: htmlFixtureName(sample, theme),
    html: await renderSample(sample, theme),
  }))

  if (checkMode) {
    const problems: string[] = []
    for (const fixture of fixtures) {
      const path = join(FIXTURES_DIR, fixtureFileName(fixture.sample))
      if (!existsSync(path)) {
        problems.push(`MISSING   ${fixtureFileName(fixture.sample)} (new sample? run fixtures:update)`)
        continue
      }
      if (readFileSync(path, 'utf-8') !== serialize(fixture)) {
        problems.push(`CHANGED   ${fixtureFileName(fixture.sample)} (CLI output diverged from committed fixture)`)
      }
    }
    for (const { name, html } of rendered) {
      const path = join(HTML_FIXTURES_DIR, name)
      if (!existsSync(path)) {
        problems.push(`MISSING   html/${name} (new sample? run fixtures:update)`)
      } else if (readFileSync(path, 'utf-8') !== html) {
        problems.push(`CHANGED   html/${name} (render output diverged from committed fixture)`)
      }
    }

    const expected = new Set(samples.map(fixtureFileName))
    for (const existing of readdirSync(FIXTURES_DIR).filter(f => f.endsWith('.json')).sort()) {
      if (!expected.has(existing)) {
        problems.push(`ORPHANED  ${existing} (sample was removed? run fixtures:update and delete it)`)
      }
    }
    const expectedHtml = new Set(rendered.map(r => r.name))
    for (const existing of existsSync(HTML_FIXTURES_DIR) ? readdirSync(HTML_FIXTURES_DIR).sort() : []) {
      if (!expectedHtml.has(existing)) {
        problems.push(`ORPHANED  html/${existing} (sample was removed? run fixtures:update and delete it)`)
      }
    }

    if (problems.length > 0) {
      console.error(`\nFixture drift detected (${problems.length} file(s)):`)
      for (const p of problems) console.error(`  ${p}`)
      console.error(`\nRun 'npm run fixtures:update -w tests/rendering' and commit the result.`)
      process.exit(1)
    }
    console.log(`All ${fixtures.length + rendered.length} fixtures match the CLI output.`)
    return
  }

  mkdirSync(FIXTURES_DIR, { recursive: true })
  for (const fixture of fixtures) {
    writeFileSync(join(FIXTURES_DIR, fixtureFileName(fixture.sample)), serialize(fixture))
    console.log(`  wrote fixtures/${fixtureFileName(fixture.sample)}`)
  }

  mkdirSync(HTML_FIXTURES_DIR, { recursive: true })
  for (const { name, html } of rendered) {
    writeFileSync(join(HTML_FIXTURES_DIR, name), html)
    console.log(`  wrote fixtures/html/${name}`)
  }
  console.log(`Done: ${fixtures.length} JSON + ${rendered.length} HTML fixtures.`)
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
