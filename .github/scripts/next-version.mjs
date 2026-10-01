// Computes the next prerelease version to publish, comparing versions with
// semver precedence (so 0.1.0-alpha.10 > 0.1.0-alpha.9) across every listed
// package, so that all packages of one registry are published at one version.
//
// Usage:
//   node next-version.mjs --registry npm|nuget [--preid alpha] [--base 0.1.0] [--version X] pkg...
//   node next-version.mjs --self-test
//
// --version short-circuits the registry lookup (validated, printed as-is); use
// it to publish npm and NuGet at the same version. Registry errors other than
// "package not found" fail loudly instead of restarting the counter at .1.

const SEMVER = /^(\d+)\.(\d+)\.(\d+)(?:-([0-9A-Za-z.-]+))?(?:\+[0-9A-Za-z.-]+)?$/

export function parse(version) {
  const m = SEMVER.exec(version)
  if (!m) return null
  return {
    version,
    core: [Number(m[1]), Number(m[2]), Number(m[3])],
    pre: m[4] ? m[4].split('.') : [],
  }
}

function compareIdent(a, b) {
  const an = /^\d+$/.test(a), bn = /^\d+$/.test(b)
  if (an && bn) return Number(a) - Number(b)
  if (an) return -1
  if (bn) return 1
  return a < b ? -1 : a > b ? 1 : 0
}

export function compare(a, b) {
  for (let i = 0; i < 3; i++) if (a.core[i] !== b.core[i]) return a.core[i] - b.core[i]
  if (a.pre.length === 0 || b.pre.length === 0) return b.pre.length - a.pre.length
  for (let i = 0; i < Math.min(a.pre.length, b.pre.length); i++) {
    const c = compareIdent(a.pre[i], b.pre[i])
    if (c !== 0) return c
  }
  return a.pre.length - b.pre.length
}

const coreString = core => core.join('.')
const compareCore = (a, b) => compare({ core: a, pre: [] }, { core: b, pre: [] })

export function nextPrerelease(published, preid, base) {
  const versions = published.map(parse).filter(Boolean)
  const baseCore = parse(base)?.core
  if (!baseCore) throw new Error(`invalid --base ${base}`)

  const ours = versions
    .filter(v => v.pre.length === 2 && v.pre[0] === preid && /^\d+$/.test(v.pre[1]))
    .sort(compare)
  const stables = versions.filter(v => v.pre.length === 0).sort(compare)
  const top = ours.at(-1)
  const topStable = stables.at(-1)

  let core = baseCore
  let n = 1
  if (top && compareCore(top.core, core) >= 0) {
    core = top.core
    n = Number(top.pre[1]) + 1
  }
  // A stable release at (or past) this core supersedes its prereleases.
  if (topStable && compareCore(topStable.core, core) >= 0) {
    core = [topStable.core[0], topStable.core[1], topStable.core[2] + 1]
    n = 1
  }
  return `${coreString(core)}-${preid}.${n}`
}

async function fetchVersions(registry, name) {
  const url = registry === 'npm'
    ? `https://registry.npmjs.org/${name.replace('/', '%2f')}`
    : `https://api.nuget.org/v3-flatcontainer/${name.toLowerCase()}/index.json`
  const res = await fetch(url, { headers: { accept: 'application/json' } })
  if (res.status === 404) return []
  if (!res.ok) throw new Error(`${url}: HTTP ${res.status}`)
  const body = await res.json()
  return registry === 'npm' ? Object.keys(body.versions ?? {}) : body.versions
}

function selfTest() {
  const cases = [
    [['0.1.0-alpha.9', '0.1.0-alpha.10', '0.1.0-alpha.2'], '0.1.0-alpha.11'],
    [[], '0.1.0-alpha.1'],
    [['0.1.0-alpha.3', '0.1.0'], '0.1.1-alpha.1'],
    [['0.1.0-beta.4'], '0.1.0-alpha.1'],
    [['0.0.9-alpha.7'], '0.1.0-alpha.1'],
  ]
  for (const [published, expected] of cases) {
    const actual = nextPrerelease(published, 'alpha', '0.1.0')
    if (actual !== expected) throw new Error(`${JSON.stringify(published)}: expected ${expected}, got ${actual}`)
  }
  if (compare(parse('1.0.0-alpha.10'), parse('1.0.0-alpha.9')) <= 0) throw new Error('numeric prerelease order')
  if (compare(parse('1.0.0'), parse('1.0.0-alpha.1')) <= 0) throw new Error('release > prerelease')
  console.log('self-test passed')
}

async function main(argv) {
  if (argv.includes('--self-test')) return selfTest()

  const opts = { registry: undefined, preid: 'alpha', base: '0.1.0', version: '' }
  const names = []
  for (let i = 0; i < argv.length; i++) {
    const a = argv[i]
    if (a.startsWith('--')) {
      const key = a.slice(2)
      if (!(key in opts) || i + 1 >= argv.length) throw new Error(`bad option ${a}`)
      opts[key] = argv[++i]
    } else {
      names.push(a)
    }
  }

  if (opts.version) {
    if (!parse(opts.version)) throw new Error(`--version ${opts.version} is not a semver version`)
    console.log(opts.version)
    return
  }
  if (opts.registry !== 'npm' && opts.registry !== 'nuget') throw new Error('--registry must be npm or nuget')
  if (names.length === 0) throw new Error('no package names given')

  const published = (await Promise.all(names.map(n => fetchVersions(opts.registry, n)))).flat()
  console.log(nextPrerelease(published, opts.preid, opts.base))
}

main(process.argv.slice(2)).catch(err => {
  console.error(`next-version: ${err.message}`)
  process.exit(1)
})
