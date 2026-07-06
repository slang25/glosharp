// Builds the static rendering gallery from committed fixtures. Renders every
// fixture through both render paths (Shiki transformer, Expressive Code
// plugin) in dark and light themes, plus an explicit anchor-positioning
// fallback variant of the Shiki page. Never invokes the GloSharp CLI: the EC
// plugin is pointed at glosharp-stub.mjs, which serves fixture JSON.
//
// Requires the @glosharp/* packages to be built (npm run build -w ...).
import { readdirSync, readFileSync, writeFileSync, mkdirSync, copyFileSync, chmodSync } from 'node:fs'
import { join, resolve, basename } from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { codeToHtml } from 'shiki'
import { transformerGloSharpFromMap, type GloSharpResultMap } from '@glosharp/shiki'
import type { GloSharpResult } from '@glosharp/core'
import { pluginGloSharp } from '@glosharp/expressive-code'
import { ExpressiveCode, loadShikiTheme, type ExpressiveCodePlugin } from 'expressive-code'
import { toHtml } from 'expressive-code/hast'

const require = createRequire(import.meta.url)

const HERE = import.meta.dirname!
const FIXTURES_DIR = resolve(HERE, '../fixtures')
const DIST = resolve(HERE, '../gallery-dist')
const STUB = join(HERE, 'glosharp-stub.mjs')

interface Fixture {
  name: string
  source: string
  result: GloSharpResult
}

const THEMES = [
  { shiki: 'github-dark', label: 'dark' },
  { shiki: 'github-light', label: 'light' },
] as const

// Fixtures that cannot currently render through a given path due to known
// package bugs. Every entry here is a live defect that should have a tracking
// issue; the build logs each skip so coverage gaps are never silent.
const KNOWN_ISSUES: Record<string, { path: 'ec' | 'shiki'; reason: string }[]> = {
  completions: [{
    path: 'ec',
    reason: 'GloSharpCompletionAnnotation uses a zero-width inline range but renders a <ul>; EC core rejects render output whose node count differs from input (crashes the EC render)',
  }],
  'multi-line-error': [{
    path: 'ec',
    reason: 'GloSharpErrorAnnotation with messageOnly returns nodesToTransform plus a message box; EC core rejects render output whose node count differs from input (crashes the EC render)',
  }],
}

function knownIssue(fixture: string, path: 'ec' | 'shiki'): string | undefined {
  return KNOWN_ISSUES[fixture]?.find(i => i.path === path)?.reason
}

function loadFixtures(): Fixture[] {
  return readdirSync(FIXTURES_DIR)
    .filter(f => f.endsWith('.json'))
    .sort()
    .map(f => {
      const parsed = JSON.parse(readFileSync(join(FIXTURES_DIR, f), 'utf-8'))
      return { name: basename(f, '.json'), source: parsed.source, result: parsed.result }
    })
}

function caseSection(id: string, inner: string): string {
  return `<section class="case" data-gallery-case="${id}">\n<h2>${id}</h2>\n${inner}\n</section>`
}

function page(title: string, themeAttr: 'dark' | 'light', head: string, body: string): string {
  return `<!DOCTYPE html>
<html lang="en" data-theme="${themeAttr}">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>${title}</title>
<link rel="stylesheet" href="assets/gallery.css">
${head}
</head>
<body>
<h1>${title}</h1>
${body}
<script type="module" src="assets/gallery-client.js"></script>
</body>
</html>`
}

// Every rendered case must actually contain GloSharp output; a silent
// fixture-lookup failure must fail the build, not produce an empty gallery.
function assertGloSharpRendered(html: string, caseId: string): void {
  if (!html.includes('glosharp-')) {
    throw new Error(`Case ${caseId} rendered without any GloSharp output — fixture lookup failed?`)
  }
}

async function buildShikiPages(fixtures: Fixture[]): Promise<Record<string, string>> {
  const resultMap: GloSharpResultMap = new Map()
  for (const f of fixtures) {
    resultMap.set(createHash('sha256').update(f.source).digest('hex'), f.result)
  }

  const styleCss = readFileSync(require.resolve('@glosharp/shiki/style.css'), 'utf-8')
  const pages: Record<string, string> = {}

  for (const theme of THEMES) {
    const sections: string[] = []
    for (const f of fixtures) {
      const id = `shiki/${f.name}/${theme.label}`
      const html = await codeToHtml(f.source, {
        lang: 'csharp',
        theme: theme.shiki,
        transformers: [transformerGloSharpFromMap(resultMap)],
      })
      assertGloSharpRendered(html, id)
      sections.push(caseSection(id, html))
    }
    pages[`shiki-${theme.label}.html`] = page(
      `GloSharp gallery — Shiki (${theme.label})`,
      theme.label,
      `<style>\n${styleCss}\n</style>`,
      sections.join('\n'),
    )
  }

  // Fallback variant: simulate a browser without CSS Anchor Positioning by
  // stripping the anchor wiring, independent of the running browser's support.
  const darkSections: string[] = []
  for (const f of fixtures) {
    const id = `shiki-fallback/${f.name}/dark`
    const html = await codeToHtml(f.source, {
      lang: 'csharp',
      theme: 'github-dark',
      transformers: [transformerGloSharpFromMap(resultMap)],
    })
    const stripped = html
      .replace(/anchor-name:\s*--th-\d+;?/g, '')
      .replace(/position-anchor:\s*--th-\d+;?/g, '')
    assertGloSharpRendered(stripped, id)
    darkSections.push(caseSection(id, stripped))
  }
  pages['shiki-fallback.html'] = page(
    'GloSharp gallery — Shiki (anchor-positioning fallback)',
    'dark',
    `<style>\n${styleCss}\n</style>\n<style>\n.glosharp-popup { position-area: none !important; position-try-fallbacks: none !important; }\n</style>`,
    darkSections.join('\n'),
  )

  return pages
}

async function buildEcPages(fixtures: Fixture[]): Promise<Record<string, string>> {
  const ec = new ExpressiveCode({
    themes: [await loadShikiTheme('github-dark'), await loadShikiTheme('github-light')],
    themeCssSelector: (theme) => `[data-theme='${theme.type}']`,
    useDarkModeMediaQuery: false,
    // The plugin's hook typings are looser than EC core's; runtime shape matches.
    plugins: [pluginGloSharp({ executable: STUB }) as unknown as ExpressiveCodePlugin],
  })

  const baseStyles = await ec.getBaseStyles()
  const themeStyles = await ec.getThemeStyles()
  const jsModules = await ec.getJsModules()

  const blockStyles = new Set<string>()
  const blocks: Record<string, string> = {}
  for (const f of fixtures) {
    const issue = knownIssue(f.name, 'ec')
    if (issue) {
      console.warn(`SKIPPED  ec/${f.name}: ${issue}`)
      continue
    }
    const { renderedGroupAst, styles } = await ec.render({
      code: f.source.replace(/\n$/, ''),
      language: 'csharp',
    })
    for (const s of styles) blockStyles.add(s)
    const html = toHtml(renderedGroupAst)
    assertGloSharpRendered(html, `ec/${f.name}`)
    blocks[f.name] = html
  }

  const head = [
    `<style>\n${baseStyles}\n</style>`,
    `<style>\n${themeStyles}\n</style>`,
    `<style>\n${[...blockStyles].join('\n')}\n</style>`,
    ...jsModules.map(m => `<script type="module">\n${m}\n</script>`),
  ].join('\n')

  const pages: Record<string, string> = {}
  for (const theme of THEMES) {
    const sections = fixtures
      .filter(f => blocks[f.name] !== undefined)
      .map(f => caseSection(`ec/${f.name}/${theme.label}`, blocks[f.name]))
    pages[`ec-${theme.label}.html`] = page(
      `GloSharp gallery — Expressive Code (${theme.label})`,
      theme.label,
      head,
      sections.join('\n'),
    )
  }
  return pages
}

async function main() {
  chmodSync(STUB, 0o755)
  const fixtures = loadFixtures()
  if (fixtures.length === 0) throw new Error(`No fixtures in ${FIXTURES_DIR}; run fixtures:update first`)

  mkdirSync(join(DIST, 'assets'), { recursive: true })

  const pages = {
    ...(await buildShikiPages(fixtures)),
    ...(await buildEcPages(fixtures)),
  }

  const links = Object.keys(pages).sort().map(p => `<li><a href="${p}">${p}</a></li>`).join('\n')
  pages['index.html' as keyof typeof pages] = page(
    'GloSharp rendering gallery',
    'dark',
    '',
    `<p>Deterministic rendering of every fixture through each render path.
Add <code>?static</code> to disable animations, <code>?pin=&lt;case-id&gt;&amp;token=&lt;n&gt;</code> to force a popup open.</p>\n<ul>\n${links}\n</ul>`,
  )

  for (const [name, html] of Object.entries(pages)) {
    writeFileSync(join(DIST, name), html)
  }

  copyFileSync(
    require.resolve('@fontsource/jetbrains-mono/files/jetbrains-mono-latin-400-normal.woff2'),
    join(DIST, 'assets/jetbrains-mono.woff2'),
  )
  copyFileSync(join(HERE, 'gallery-client.js'), join(DIST, 'assets/gallery-client.js'))

  writeFileSync(join(DIST, 'assets/gallery.css'), `
@font-face {
  font-family: 'JetBrains Mono';
  src: url('jetbrains-mono.woff2') format('woff2');
  font-weight: 400;
  font-style: normal;
}

body {
  margin: 24px;
  background: #101014;
  color: #ddd;
  font-family: system-ui, sans-serif;
}
html[data-theme='light'] body {
  background: #f6f6f6;
  color: #222;
}

h1 { font-size: 18px; }

.case { max-width: 900px; margin: 32px 0; }
.case h2 {
  font-size: 12px;
  font-family: 'JetBrains Mono', monospace;
  font-weight: 400;
  opacity: 0.65;
}

/* Pinned font for pixel determinism across machines */
pre, code, .expressive-code {
  font-family: 'JetBrains Mono', monospace !important;
}

pre.shiki {
  overflow-x: auto;
  padding: 12px;
  border-radius: 6px;
}
`)

  const fixtureCount = fixtures.length
  const pageCount = Object.keys(pages).length
  console.log(`Gallery built: ${pageCount} pages, ${fixtureCount} fixtures → ${DIST}`)
}

main().catch((err) => {
  console.error(err)
  process.exit(1)
})
