/**
 * Standalone rendering script — generates a self-contained HTML page from C# snippets.
 *
 * Prerequisites:
 *   npm install twohash @twohash/shiki shiki
 *   dotnet tool install -g twohash
 *
 * Usage:
 *   node render.mjs               # renders all .cs files in this directory
 *   node render.mjs snippet.cs    # renders a single file
 */

import { readFileSync, writeFileSync, readdirSync } from 'node:fs'
import { basename, resolve } from 'node:path'
import { codeToHtml } from 'shiki'
import { processTwohashCode, transformerTwohashWithResult } from '@twohash/shiki'

const files = process.argv.slice(2)
const csFiles = files.length > 0
  ? files
  : readdirSync(import.meta.dirname).filter(f => f.endsWith('.cs')).sort()

if (csFiles.length === 0) {
  console.log('No .cs files found.')
  process.exit(0)
}

const sections = []

for (const file of csFiles) {
  const code = readFileSync(resolve(import.meta.dirname, file), 'utf-8')
  console.log(`Processing ${file}...`)

  const result = await processTwohashCode(code)
  const transformers = result ? [transformerTwohashWithResult(result)] : []

  const html = await codeToHtml(result?.code ?? code, {
    lang: 'csharp',
    themes: { light: 'github-light', dark: 'github-dark' },
    transformers,
  })

  sections.push(`<h2>${basename(file)}</h2>\n${html}`)
}

const page = `<!DOCTYPE html>
<html lang="en">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Twohash Standalone Example</title>
  <style>
    body {
      font-family: system-ui, -apple-system, sans-serif;
      max-width: 48rem;
      margin: 2rem auto;
      padding: 0 1rem;
      line-height: 1.6;
      color: #d4d4d4;
      background: #1e1e1e;
    }

    h1 { color: #fff; }
    h2 { color: #ccc; margin-top: 2rem; font-size: 1.1rem; }

    pre {
      padding: 1rem;
      border-radius: 0.5rem;
      overflow-x: auto;
    }

    /* Twohash hover styles */
    .twohash-hover {
      position: relative;
      border-bottom: 1px dotted transparent;
      transition: border-color 0.3s ease;
      cursor: pointer;
    }

    @media (prefers-reduced-motion: reduce) {
      .twohash-hover { transition: none; }
    }

    /* Container hover: subtle underline on all hoverable tokens */
    pre:hover .twohash-hover:not(:hover) {
      border-bottom-color: color-mix(in srgb, currentColor 40%, transparent);
    }

    /* Token hover: strong underline */
    .twohash-hover:hover {
      border-bottom-color: currentColor;
    }

    .twohash-popup {
      display: none;
      position: fixed;
      position-area: top;
      margin-bottom: 4px;
      z-index: 100;
      max-width: 500px;
      padding: 8px 12px;
      border: 1px solid #3c3c3c;
      border-radius: 4px;
      background: #1e1e1e;
      color: #d4d4d4;
      font-size: 0.875em;
      line-height: 1.5;
      white-space: pre-wrap;
      box-shadow: 0 2px 8px rgba(0, 0, 0, 0.4);
    }

    .twohash-hover:hover + .twohash-popup,
    .twohash-popup:hover {
      display: block;
    }

    .twohash-popup-code { font-family: inherit; }

    .twohash-popup-docs {
      margin-top: 6px;
      padding-top: 6px;
      border-top: 1px solid #3c3c3c;
    }

    /* VS Code-like syntax colors */
    .twohash-keyword { color: #569cd6; }
    .twohash-className, .twohash-structName { color: #4ec9b0; }
    .twohash-interfaceName, .twohash-enumName { color: #b8d7a3; }
    .twohash-methodName { color: #dcdcaa; }
    .twohash-propertyName, .twohash-localName, .twohash-parameterName { color: #9cdcfe; }
    .twohash-punctuation, .twohash-operator, .twohash-text { color: #d4d4d4; }

    /* Error display */
    .twohash-error-message {
      display: block;
      padding: 2px 8px;
      margin-top: 2px;
      background: rgba(244, 71, 71, 0.1);
      border-left: 3px solid #f44747;
      color: #f44747;
      font-size: 0.85em;
    }

    .twohash-error-code { font-weight: bold; }
    a.twohash-error-code { color: inherit; text-decoration: none; }
    a.twohash-error-code:hover { text-decoration: underline; }

    /* Completion list */
    .twohash-completion-list {
      list-style: none;
      margin: 4px 0 0 0;
      padding: 4px 0;
      border: 1px solid #3c3c3c;
      border-radius: 4px;
      background: #252526;
      font-size: 0.875em;
      max-height: 200px;
      overflow-y: auto;
    }

    .twohash-completion-item {
      display: flex;
      gap: 8px;
      padding: 2px 8px;
      align-items: center;
    }

    .twohash-completion-kind {
      font-size: 0.75em;
      opacity: 0.7;
      min-width: 60px;
    }
  </style>
</head>
<body>
  <h1>Twohash Standalone Example</h1>
  <p>Hover over the code to reveal interactive tokens, then hover a token to see its type.</p>
  ${sections.join('\n  ')}
</body>
</html>`

const outPath = resolve(import.meta.dirname, 'output.html')
writeFileSync(outPath, page, 'utf-8')
console.log(`\nWrote ${outPath}`)
