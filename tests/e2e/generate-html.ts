import { execFileSync } from 'node:child_process'
import { writeFileSync } from 'node:fs'
import { join } from 'node:path'
import { codeToHtml } from 'shiki'

const CLI_PROJECT = join(import.meta.dirname!, '../../src/TwoHash.Cli/TwoHash.Cli.csproj')
const SAMPLES_DIR = join(import.meta.dirname!, '../../samples')
const CSS = `
<style>
body { font-family: -apple-system, sans-serif; background: #1a1a2e; color: #e0e0e0; padding: 40px; }
h1 { color: #7c3aed; }
h2 { color: #a78bfa; margin-top: 32px; }
.sample { margin: 16px 0; position: relative; }
pre { border-radius: 8px; overflow-x: auto; }

.twohash-hover {
  position: relative;
  border-bottom: 1px dotted #a78bfa;
  cursor: pointer;
}
.twohash-popup {
  display: none;
  position: fixed;
  inset-area: top;
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
  box-shadow: 0 2px 8px rgba(0,0,0,0.4);
}
.twohash-hover:hover + .twohash-popup,
.twohash-popup:hover { display: block; }
.twohash-popup-code { font-family: 'SF Mono', 'Cascadia Code', monospace; }
.twohash-popup-docs { margin-top: 6px; padding-top: 6px; border-top: 1px solid #3c3c3c; font-style: italic; color: #9cdcfe; }
.twohash-error-message { display: block; padding: 2px 8px; margin-top: 2px; background: rgba(244,71,71,0.1); border-left: 3px solid #f44747; color: #f44747; font-size: 0.85em; }
.twohash-error-code { font-weight: bold; }
.twohash-keyword { color: #569cd6; }
.twohash-className, .twohash-structName { color: #4ec9b0; }
.twohash-interfaceName, .twohash-enumName { color: #b8d7a3; }
.twohash-methodName { color: #dcdcaa; }
.twohash-propertyName, .twohash-fieldName, .twohash-localName, .twohash-parameterName { color: #9cdcfe; }
.twohash-punctuation, .twohash-operator { color: #d4d4d4; }
.twohash-text { color: #d4d4d4; }
</style>
`

function runCli(file: string): any {
  const stdout = execFileSync('dotnet', ['run', '--project', CLI_PROJECT, '--', 'process', file], {
    encoding: 'utf-8',
    timeout: 30000,
  })
  return JSON.parse(stdout)
}

function findLines(node: any): any[] {
  const lines: any[] = []
  function walk(n: any) {
    if (n.tagName === 'span' && n.properties?.class === 'line') lines.push(n)
    if (n.children) for (const c of n.children) walk(c)
  }
  walk(node)
  return lines
}

function getText(node: any): string {
  if (node.type === 'text') return node.value ?? ''
  return (node.children ?? []).map(getText).join('')
}

async function renderSample(name: string): Promise<string> {
  const result = runCli(join(SAMPLES_DIR, name))

  const html = await codeToHtml(result.code, {
    lang: 'csharp',
    theme: 'github-dark',
    transformers: [{
      name: 'twohash-inject',
      root(hast: any) {
        const lines = findLines(hast)
        let idx = 0
        for (const hover of result.hovers) {
          const line = lines[hover.line]
          if (!line?.children) continue
          const anchor = `--th-${name.replace('.cs','')}-${idx++}`
          for (let i = 0; i < line.children.length; i++) {
            if (getText(line.children[i]).includes(hover.targetText)) {
              const parts = hover.parts.map((p: any) => ({
                type: 'element', tagName: 'span',
                properties: { class: `twohash-${p.kind}` },
                children: [{ type: 'text', value: p.text }],
              }))
              line.children.splice(i, 1,
                { type: 'element', tagName: 'span', properties: { class: 'twohash-hover', style: `anchor-name: ${anchor}` }, children: [line.children[i]] },
                { type: 'element', tagName: 'div', properties: { class: 'twohash-popup', style: `position-anchor: ${anchor}` },
                  children: [{ type: 'element', tagName: 'code', properties: { class: 'twohash-popup-code' }, children: parts },
                    ...(hover.docs ? [{ type: 'element', tagName: 'div', properties: { class: 'twohash-popup-docs' }, children: [{ type: 'text', value: hover.docs }] }] : []),
                  ],
                },
              )
              break
            }
          }
        }
        // Inject error messages
        for (const error of result.errors) {
          if (error.expected) continue
          const line = lines[error.line]
          if (!line?.children) continue
          line.children.push({
            type: 'element', tagName: 'div', properties: { class: 'twohash-error-message' },
            children: [
              { type: 'element', tagName: 'span', properties: { class: 'twohash-error-code' }, children: [{ type: 'text', value: error.code }] },
              { type: 'text', value: `: ${error.message}` },
            ],
          })
        }
      },
    }],
  })

  return `<div class="sample"><h2>${name}</h2>${html}</div>`
}

async function main() {
  const samples = ['local-variables.cs', 'method-calls.cs', 'overloads.cs', 'nullable.cs', 'cut-marker.cs', 'hide-show.cs']
  const blocks = await Promise.all(samples.map(renderSample))

  const page = `<!DOCTYPE html>
<html lang="en">
<head>
<meta charset="UTF-8">
<meta name="viewport" content="width=device-width, initial-scale=1.0">
<title>Twohash Visual Smoke Test</title>
${CSS}
</head>
<body>
<h1>Twohash Visual Smoke Test</h1>
<p>Hover over dotted-underlined tokens to see type information popups.</p>
${blocks.join('\n')}
</body>
</html>`

  const outPath = join(import.meta.dirname!, 'smoke-test.html')
  writeFileSync(outPath, page)
  console.log(`Generated: ${outPath}`)
}

main().catch(console.error)
