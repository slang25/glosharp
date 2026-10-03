// Checks that every published npm package actually contains the files its
// package.json points at (main, types, bin, every "exports" target) plus a
// README. Inside the monorepo, workspace symlinks hide a missing file; a
// registry install does not (e.g. an exported stylesheet that `files` omits).
//
// Usage: node .github/scripts/check-pack.mjs [packageDir ...]
// Run after `npm run build` so dist/ exists.
import { execFileSync } from 'node:child_process'
import { readFileSync, readdirSync, existsSync } from 'node:fs'
import { join, posix } from 'node:path'

const root = new URL('../..', import.meta.url).pathname.replace(/^\/([A-Za-z]:)/, '$1')
const dirs = process.argv.length > 2
  ? process.argv.slice(2)
  : readdirSync(join(root, 'packages'))
      .map(name => join('packages', name))
      .filter(dir => existsSync(join(root, dir, 'package.json')))

function targets(value, out = []) {
  if (typeof value === 'string') out.push(value)
  else if (Array.isArray(value)) value.forEach(v => targets(v, out))
  else if (value && typeof value === 'object') Object.values(value).forEach(v => targets(v, out))
  return out
}

const normalize = p => posix.normalize(p.replace(/\\/g, '/')).replace(/^\.\//, '')

let failed = false
for (const dir of dirs) {
  const pkg = JSON.parse(readFileSync(join(root, dir, 'package.json'), 'utf-8'))
  if (pkg.private) continue

  const out = execFileSync('npm', ['pack', '--dry-run', '--json', '--ignore-scripts'], {
    cwd: join(root, dir),
    encoding: 'utf-8',
    shell: process.platform === 'win32',
  })
  const [{ files }] = JSON.parse(out)
  const packed = new Set(files.map(f => normalize(f.path)))

  const expected = new Set([
    ...targets(pkg.main),
    ...targets(pkg.types),
    ...targets(typeof pkg.bin === 'string' ? pkg.bin : Object.values(pkg.bin ?? {})),
    ...targets(pkg.exports),
  ].filter(t => !t.includes('*')).map(normalize))

  const missing = [...expected].filter(p => !packed.has(p))
  if (!packed.has('README.md')) missing.push('README.md')

  if (missing.length > 0) {
    failed = true
    console.error(`${pkg.name}: referenced but not in the tarball:\n  ${missing.join('\n  ')}`)
  } else {
    console.log(`${pkg.name}: ok (${packed.size} files)`)
  }
}

process.exit(failed ? 1 : 0)
