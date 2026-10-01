// Checks a release's packed artifacts before anything is published:
//
//   - GloSharp.Core.<v>.nupkg and GloSharp.Cli.<v>.nupkg exist
//   - one tarball per published npm package (packages/*), each with
//     version <v> and its @glosharp/* dependencies pinned to <v>
//   - @glosharp/core's EXPECTED_CLI_VERSION is <v>
//
// With --smoke it also installs the four tarballs into a scratch project (the
// way a user would, peers from the registry) and runs one snippet through
// @glosharp/core against the `glosharp` on PATH, which must report <v> and
// produce a hover.
//
// Usage: node .github/scripts/check-release.mjs <artifacts-dir> [--version <v>] [--smoke]
//   <artifacts-dir> holds nupkg/*.nupkg and npm/*.tgz. --version defaults to
//   the version in src/Directory.Build.props.
import { execFileSync } from 'node:child_process'
import { existsSync, mkdtempSync, readdirSync, readFileSync, rmSync, writeFileSync } from 'node:fs'
import { tmpdir } from 'node:os'
import { join, resolve } from 'node:path'

const root = resolve(new URL('../..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1'))
let dir = 'artifacts'
let version
let smoke = false
const args = process.argv.slice(2)
for (let i = 0; i < args.length; i++) {
  if (args[i] === '--smoke') smoke = true
  else if (args[i] === '--version') version = args[++i]
  else dir = args[i]
}
dir = resolve(dir)
version ??= execFileSync(process.execPath, [join(root, 'scripts', 'version.mjs')], { encoding: 'utf8' }).trim()

const isWindows = process.platform === 'win32'
const run = (cmd, argv, opts = {}) =>
  execFileSync(cmd, argv, { encoding: 'utf8', shell: isWindows, stdio: ['ignore', 'pipe', 'inherit'], ...opts })

const failures = []
const fail = (msg) => failures.push(msg)

// NuGet
for (const id of ['GloSharp.Core', 'GloSharp.Cli']) {
  const file = join(dir, 'nupkg', `${id}.${version}.nupkg`)
  if (existsSync(file)) console.log(`${id}: ${file}`)
  else fail(`missing ${file}`)
}

// npm
const packages = readdirSync(join(root, 'packages'))
  .map((name) => join(root, 'packages', name, 'package.json'))
  .filter(existsSync)
  .map((file) => JSON.parse(readFileSync(file, 'utf8')))
  .filter((pkg) => !pkg.private)
const names = new Set(packages.map((p) => p.name))
const tarballs = []

for (const pkg of packages) {
  // npm pack names @scope/name@v as scope-name-v.tgz
  const file = join(dir, 'npm', `${pkg.name.replace(/^@/, '').replace('/', '-')}-${version}.tgz`)
  if (!existsSync(file)) {
    fail(`missing ${file}`)
    continue
  }
  tarballs.push(file)
  const packed = JSON.parse(run('tar', ['-xzOf', file, 'package/package.json']))
  if (packed.version !== version) fail(`${file}: version ${packed.version}, expected ${version}`)
  for (const field of ['dependencies', 'peerDependencies', 'optionalDependencies']) {
    for (const [dep, range] of Object.entries(packed[field] ?? {})) {
      if (names.has(dep) && range !== version) fail(`${file}: ${field}.${dep} is ${range}, expected ${version}`)
    }
  }
  if (pkg.name === '@glosharp/core') {
    const generated = run('tar', ['-xzOf', file, 'package/dist/version.generated.js'])
    if (!generated.includes(`'${version}'`)) fail(`${file}: dist/version.generated.js does not expect CLI ${version}`)
  }
  console.log(`${packed.name}@${packed.version}: ${file}`)
}

if (failures.length === 0 && smoke) {
  const scratch = mkdtempSync(join(tmpdir(), 'glosharp-release-smoke-'))
  try {
    writeFileSync(join(scratch, 'package.json'), JSON.stringify({ name: 'smoke', private: true, type: 'module' }))
    run('npm', ['install', '--no-audit', '--no-fund', '--loglevel=error', ...tarballs], { cwd: scratch, stdio: 'inherit' })
    writeFileSync(
      join(scratch, 'smoke.mjs'),
      `
import { createGloSharp, resolveExecutable, checkCliVersion, EXPECTED_CLI_VERSION } from '@glosharp/core'
import '@glosharp/shiki'
import '@glosharp/expressive-code'
import '@glosharp/gitbook'

if (EXPECTED_CLI_VERSION !== ${JSON.stringify(version)}) throw new Error('EXPECTED_CLI_VERSION is ' + EXPECTED_CLI_VERSION)
const warnings = []
await checkCliVersion(await resolveExecutable(undefined), { warn: (m) => warnings.push(m), env: {} })
if (warnings.length) throw new Error('version warning: ' + warnings.join('\\n'))

const result = await createGloSharp().process({ code: 'var answer = 42;\\n//  ^?\\n' })
if (!result.meta.compileSucceeded) throw new Error('snippet did not compile: ' + JSON.stringify(result.errors))
if (!result.hovers.some((h) => h.text.includes('int answer'))) throw new Error('no hover: ' + JSON.stringify(result.hovers))
console.log('smoke test passed: ' + result.hovers[0].text)
`,
    )
    run(process.execPath, ['smoke.mjs'], { cwd: scratch, stdio: 'inherit' })
  } catch (error) {
    fail(`smoke test failed: ${error.message}`)
  } finally {
    rmSync(scratch, { recursive: true, force: true })
  }
}

if (failures.length > 0) {
  console.error(`\nRelease artifacts for ${version} are not right:\n  ${failures.join('\n  ')}`)
  process.exit(1)
}
console.log(`\nRelease artifacts for ${version} look right.`)
