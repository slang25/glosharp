// Builds the static rendering gallery from committed fixtures. Renders every
// fixture through both render paths (Shiki transformer, Expressive Code
// plugin) in dark and light themes, plus an explicit anchor-positioning
// fallback variant of the Shiki page and a page that drives the GitBook
// webframe shell. Never invokes the GloSharp CLI: the EC plugin is pointed at
// glosharp-stub.mjs, which serves fixture JSON.
//
// Requires the @glosharp/* packages to be built (npm run build -w ...).
import { readdirSync, readFileSync, writeFileSync, mkdirSync, copyFileSync, chmodSync, existsSync } from 'node:fs'
import { join, resolve, basename } from 'node:path'
import { createRequire } from 'node:module'
import { createHash } from 'node:crypto'
import { codeToHtml } from 'shiki'
import { transformerGloSharpFromMap, type GloSharpResultMap } from '@glosharp/shiki'
import type { GloSharpResult } from '@glosharp/core'
import { pluginGloSharp } from '@glosharp/expressive-code'
import { GITBOOK_HOST_SCRIPT, renderFrameShell, renderWebframeIframe, snippetKey } from '@glosharp/gitbook'
import { ExpressiveCode, loadShikiTheme, type ExpressiveCodePlugin } from 'expressive-code'
import { toHtml } from 'expressive-code/hast'

const require = createRequire(import.meta.url)

const HERE = import.meta.dirname!
const FIXTURES_DIR = resolve(HERE, '../fixtures')
const DIST = resolve(HERE, '../gallery-dist')
const STUB = join(HERE, 'glosharp-stub.mjs')
const HTML_FIXTURES_DIR = resolve(HERE, '../fixtures/html')
const SHIKI_STYLE_CSS = readFileSync(require.resolve('@glosharp/shiki/style.css'), 'utf-8')

interface Fixture {
  name: string
  source: string
  result: GloSharpResult
  /** `glosharp render` output per theme, from fixtures/html (may be absent). */
  html: Record<string, string>
}

const THEMES = [
  { shiki: 'github-dark', label: 'dark' },
  { shiki: 'github-light', label: 'light' },
] as const

// Fixtures that cannot currently render through a given path due to known
// package bugs. Every entry here is a live defect that should have a tracking
// issue; the build logs each skip so coverage gaps are never silent.
const KNOWN_ISSUES: Record<string, { path: 'ec' | 'shiki'; reason: string }[]> = {}

function knownIssue(fixture: string, path: 'ec' | 'shiki'): string | undefined {
  return KNOWN_ISSUES[fixture]?.find(i => i.path === path)?.reason
}

function loadFixtures(): Fixture[] {
  return readdirSync(FIXTURES_DIR)
    .filter(f => f.endsWith('.json'))
    .sort()
    .map(f => {
      const parsed = JSON.parse(readFileSync(join(FIXTURES_DIR, f), 'utf-8'))
      const name = basename(f, '.json')
      const html: Record<string, string> = {}
      for (const theme of THEMES) {
        const file = join(HTML_FIXTURES_DIR, `${name}.${theme.shiki}.html`)
        if (existsSync(file)) html[theme.shiki] = readFileSync(file, 'utf-8')
      }
      return { name, source: parsed.source, result: parsed.result, html }
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

  const styleCss = SHIKI_STYLE_CSS
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
  // Force-enable the stylesheet's own @supports-not fallback block (in
  // addition to the stripped inline anchor styles above), so the page shows
  // exactly what a browser without CSS Anchor Positioning renders.
  const fallbackCss = styleCss.replace('@supports not (anchor-name: --a)', '@supports (color: red)')
  if (fallbackCss === styleCss) {
    throw new Error('shiki style.css no longer contains the @supports-not anchor fallback block')
  }
  pages['shiki-fallback.html'] = page(
    'GloSharp gallery — Shiki (anchor-positioning fallback)',
    'dark',
    `<style>\n${fallbackCss}\n</style>`,
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

/**
 * The standalone renderer's own output, as shipped.
 *
 * This is the only render path whose markup and CSS travel together, and the
 * only one a GitBook reader ever sees — but it was long the only one with no
 * browser coverage, which is how popups that could never open shipped in it.
 */
function buildStandalonePages(fixtures: Fixture[]): Record<string, string> {
  const pages: Record<string, string> = {}

  for (const theme of THEMES) {
    const sections: string[] = []
    for (const f of fixtures) {
      const html = f.html[theme.shiki]
      if (!html) {
        console.warn(`SKIPPED  standalone/${f.name}/${theme.label}: no HTML fixture — run fixtures:update`)
        continue
      }
      const id = `standalone/${f.name}/${theme.label}`
      assertGloSharpRendered(html, id)
      sections.push(caseSection(id, html))
    }
    if (sections.length === 0) {
      throw new Error('No HTML fixtures found; run fixtures:update -w tests/rendering')
    }
    pages[`standalone-${theme.label}.html`] = page(
      `GloSharp gallery — standalone renderer (${theme.label})`,
      theme.label,
      // Nothing to add: `glosharp render` output carries its own styles.
      '',
      sections.join('\n'),
    )
  }

  return pages
}

/**
 * The GitBook webframe shell, driven the way GitBook drives it.
 *
 * The shell is what makes a rendered snippet survive inside an iframe: it hashes
 * the fence body, pulls the matching artifact, and reports a height back to the
 * host — including extra height so an open popup is not clipped by the frame
 * edge. None of that is observable from the fragment alone, so it needs a page
 * of its own. The published artifacts here come from the Shiki path (same
 * markup contract as `glosharp render`, no .NET needed) with the stylesheet
 * inlined so each fragment is self-contained.
 */
function buildGitBookFramePages(fixtures: Fixture[]): {
  pages: Record<string, string>
  artifacts: Record<string, string>
} {
  const artifacts: Record<string, string> = {}
  const cases: { id: string; state: Record<string, string> }[] = []

  // Publish the same bytes CI would: `glosharp render` output, keyed the same way.
  for (const f of fixtures) {
    const html = f.html['github-dark']
    if (!html) continue
    artifacts[`github-dark/${snippetKey(f.source)}.html`] = html
    cases.push({
      id: `gitbook-frame/${f.name}/dark`,
      state: { content: f.source, artifacts: '/gitbook-artifacts', theme: 'github-dark' },
    })
  }

  // No artifacts URL configured: the shell never fetches, so this belongs on the
  // main page. A *missing* artifact necessarily 404s, which the console-cleanliness
  // fixture treats as a page defect — so it gets a page of its own.
  cases.push({
    id: 'gitbook-frame/no-artifacts-url/dark',
    state: { content: 'var x = 1;', artifacts: '', theme: 'github-dark' },
  })

  // Inline and classic, not a deferred module: the listener has to exist before
  // the browser parses the first iframe, or that frame's readiness announcement
  // lands on nothing.
  const head = `<style>
.webframe { width: 100%; border: 0; display: block; height: 120px; }
</style>
<script>
${GITBOOK_HOST_SCRIPT}
</script>`

  const sections = (of: typeof cases) =>
    of
      .map(({ id, state }) =>
        caseSection(id, renderWebframeIframe(id, state, { frameUrl: 'gitbook-frame-shell.html' })),
      )
      .join('\n')

  return {
    pages: {
      'gitbook-frame-shell.html': renderFrameShell(),
      'gitbook-frame.html': page(
        'GloSharp gallery — GitBook webframe',
        'dark',
        head,
        sections(cases),
      ),
      'gitbook-frame-unpublished.html': page(
        'GloSharp gallery — GitBook webframe (unpublished snippet)',
        'dark',
        head,
        sections([
          {
            id: 'gitbook-frame/unpublished/dark',
            state: {
              content: 'var neverPublished = 1;',
              artifacts: '/gitbook-artifacts',
              theme: 'github-dark',
            },
          },
        ]),
      ),
    },
    artifacts,
  }
}

async function main() {
  chmodSync(STUB, 0o755)
  const fixtures = loadFixtures()
  if (fixtures.length === 0) throw new Error(`No fixtures in ${FIXTURES_DIR}; run fixtures:update first`)

  mkdirSync(join(DIST, 'assets'), { recursive: true })

  const gitbook = buildGitBookFramePages(fixtures)
  const pages: Record<string, string> = {
    ...(await buildShikiPages(fixtures)),
    ...(await buildEcPages(fixtures)),
    ...buildStandalonePages(fixtures),
    ...gitbook.pages,
  }

  for (const [name, html] of Object.entries(gitbook.artifacts)) {
    const target = join(DIST, 'gitbook-artifacts', name)
    mkdirSync(resolve(target, '..'), { recursive: true })
    writeFileSync(target, html)
  }

  const links = Object.keys(pages).sort().map(p => `<li><a href="${p}">${p}</a></li>`).join('\n')
  pages['index.html'] = page(
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
  // The host stub is GitBook's side of the webframe contract, shared with the
  // package's own local preview so there is one definition of it.
  writeFileSync(join(DIST, 'assets/gitbook-host.js'), GITBOOK_HOST_SCRIPT)

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
