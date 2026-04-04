/**
 * Standalone rendering script — generates a self-contained HTML page from C# snippets.
 *
 * Prerequisites:
 *   npm install @glosharp/core @glosharp/shiki shiki
 *   dotnet tool install -g glosharp
 *
 * Usage:
 *   node render.mjs               # renders all .cs files in this directory
 *   node render.mjs snippet.cs    # renders a single file
 */

import { readFileSync, writeFileSync, readdirSync } from 'node:fs'
import { basename, resolve } from 'node:path'
import { codeToHtml } from 'shiki'
import { processGloSharpCode, transformerGloSharpWithResult } from '@glosharp/shiki'

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

  const result = await processGloSharpCode(code)
  const transformers = result ? [transformerGloSharpWithResult(result)] : []

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
  <title>GloSharp Standalone Example</title>
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

    /* GloSharp hover styles */
    .glosharp-hover {
      position: relative;
      border-bottom: 1px dotted transparent;
      transition: border-color 0.3s ease;
      cursor: pointer;
    }

    @media (prefers-reduced-motion: reduce) {
      .glosharp-hover { transition: none; }
    }

    /* Container hover: subtle underline on all hoverable tokens */
    pre:hover .glosharp-hover:not(:hover):not(.glosharp-hover-persistent) {
      border-bottom-color: color-mix(in srgb, currentColor 40%, transparent);
    }

    /* Token hover: strong underline */
    .glosharp-hover:hover {
      border-bottom-color: currentColor;
    }

    .glosharp-hover-persistent {
      border-bottom-color: currentColor;
    }

    .glosharp-popup {
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

    .glosharp-hover:hover + .glosharp-popup,
    .glosharp-popup:hover {
      display: block;
    }

    .glosharp-popup-code { font-family: inherit; }

    .glosharp-popup-docs {
      margin-top: 6px;
      padding-top: 6px;
      border-top: 1px solid #3c3c3c;
    }

    /* VS Code-like syntax colors */
    .glosharp-keyword { color: #569cd6; }
    .glosharp-className, .glosharp-structName { color: #4ec9b0; }
    .glosharp-interfaceName, .glosharp-enumName { color: #b8d7a3; }
    .glosharp-methodName { color: #dcdcaa; }
    .glosharp-propertyName, .glosharp-localName, .glosharp-parameterName { color: #9cdcfe; }
    .glosharp-punctuation, .glosharp-operator, .glosharp-text { color: #d4d4d4; }

    /* Error display */
    .glosharp-error-message {
      display: block;
      padding: 2px 8px;
      margin-top: 2px;
      background: rgba(244, 71, 71, 0.1);
      border-left: 3px solid #f44747;
      color: #f44747;
      font-size: 0.85em;
    }

    .glosharp-error-code { font-weight: bold; }
    a.glosharp-error-code { color: inherit; text-decoration: none; }
    a.glosharp-error-code:hover { text-decoration: underline; }

    /* Completion list */
    .glosharp-completion-list {
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

    .glosharp-completion-item {
      display: flex;
      gap: 8px;
      padding: 2px 8px;
      align-items: center;
    }

    .glosharp-completion-kind {
      font-size: 0.75em;
      opacity: 0.7;
      min-width: 60px;
    }
  </style>
</head>
<body>
  <h1>GloSharp Standalone Example</h1>
  <p>Hover over the code to reveal interactive tokens, then hover a token to see its type.</p>
  ${sections.join('\n  ')}
</body>
</html>`

const outPath = resolve(import.meta.dirname, 'output.html')
writeFileSync(outPath, page, 'utf-8')
console.log(`\nWrote ${outPath}`)
