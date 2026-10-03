/**
 * Standalone rendering script: turns C# snippets into one self-contained HTML
 * page with hover popups, using Shiki and @glosharp/shiki. No framework needed.
 *
 * (If you don't need Shiki at all, the CLI can do this on its own:
 *  `glosharp render snippet.cs --standalone --output snippet.html`.)
 *
 * Prerequisites:
 *   npm install @glosharp/shiki shiki
 *   dotnet tool install --global GloSharp.Cli --prerelease
 *
 * Usage:
 *   node render.mjs               # renders all .cs files in this directory
 *   node render.mjs snippet.cs    # renders a single file
 *
 * Set GLOSHARP_EXECUTABLE to use a glosharp binary that isn't on PATH.
 */

import { readFileSync, writeFileSync, readdirSync } from 'node:fs'
import { createRequire } from 'node:module'
import { basename, resolve } from 'node:path'
import { codeToHtml } from 'shiki'
import { processGloSharpBlocks, transformerGloSharpFromMap } from '@glosharp/shiki'

const files = process.argv.slice(2)
const csFiles = files.length > 0
  ? files
  : readdirSync(import.meta.dirname).filter(f => f.endsWith('.cs')).sort()

if (csFiles.length === 0) {
  console.log('No .cs files found.')
  process.exit(0)
}

const snippets = csFiles.map(file => ({
  file,
  code: readFileSync(resolve(import.meta.dirname, file), 'utf-8'),
}))

// Step 1: run glosharp over every snippet in one batch.
console.log(`Processing ${snippets.length} snippet(s)...`)
const results = await processGloSharpBlocks(
  snippets.map(s => s.code),
  { executable: process.env.GLOSHARP_EXECUTABLE },
)

// Step 2: highlight with Shiki; the transformer looks each snippet's result up
// by its code, strips the markers and adds hovers, errors and completions.
const sections = []
for (const { file, code } of snippets) {
  const html = await codeToHtml(code, {
    lang: 'csharp',
    themes: { light: 'github-light', dark: 'github-dark' },
    transformers: [transformerGloSharpFromMap(results)],
  })
  sections.push(`<h2>${basename(file)}</h2>\n${html}`)
}

// The package's stylesheet positions and shows the popups.
const require = createRequire(import.meta.url)
const glosharpCss = readFileSync(require.resolve('@glosharp/shiki/style.css'), 'utf-8')

const page = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Glo# Standalone Example</title>
  <style>
${glosharpCss}
  </style>
  <style>
    body {
      font-family: system-ui, -apple-system, sans-serif;
      max-width: 48rem;
      margin: 2rem auto;
      padding: 0 1rem;
      line-height: 1.6;
      color: #1e1e1e;
      background: #fff;
    }

    h2 { margin-top: 2rem; font-size: 1.1rem; }

    pre {
      padding: 1rem;
      border-radius: 0.5rem;
      overflow-x: auto;
    }

    /* Shiki emits both themes; switch to the dark one when preferred. Only
       token spans carry the variables, so popup colours stay intact. */
    @media (prefers-color-scheme: dark) {
      body { color: #d4d4d4; background: #1e1e1e; }
      .shiki {
        color: var(--shiki-dark) !important;
        background-color: var(--shiki-dark-bg) !important;
      }
      .shiki span[style*='--shiki-dark'] { color: var(--shiki-dark) !important; }
    }
  </style>
</head>
<body>
  <h1>Glo# Standalone Example</h1>
  <p>Hover over the code to see each token's type and documentation.</p>
  ${sections.join('\n  ')}
</body>
</html>`

const outPath = resolve(import.meta.dirname, 'output.html')
writeFileSync(outPath, page, 'utf-8')
console.log(`\nWrote ${outPath}`)
