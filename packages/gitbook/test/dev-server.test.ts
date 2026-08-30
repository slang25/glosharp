import { describe, it, expect, beforeEach, afterEach } from 'vitest'
import { mkdir, mkdtemp, rm, writeFile } from 'node:fs/promises'
import { tmpdir } from 'node:os'
import path from 'node:path'
import { startDevServer, type DevServer } from '../src/dev-server.js'
import { snippetKey } from '../src/hash.js'

let root: string
let server: DevServer | undefined

beforeEach(async () => {
  root = await mkdtemp(path.join(tmpdir(), 'glosharp-dev-'))
})

afterEach(async () => {
  await server?.close()
  server = undefined
  await rm(root, { recursive: true, force: true })
})

async function write(relative: string, contents: string): Promise<void> {
  const target = path.join(root, relative)
  await mkdir(path.dirname(target), { recursive: true })
  await writeFile(target, contents, 'utf8')
}

const fence = (code: string) => `# Doc\n\n\`\`\`glosharp\n${code}\n\`\`\`\n`

async function start(overrides: Partial<Parameters<typeof startDevServer>[0]> = {}) {
  server = await startDevServer({
    paths: [root],
    root,
    outDir: path.join(root, '.out'),
    themes: ['github-dark'],
    port: 0,
    render: async ({ code, theme }) => `<div data-theme="${theme}">${code}</div>`,
    ...overrides,
  })
  return server
}

describe('startDevServer', () => {
  it('renders on request and serves a frame per snippet', async () => {
    await write('a.md', fence('var x = 42;'))
    await write('b.md', fence('var y = 1;'))
    const { url } = await start()

    const html = await (await fetch(url)).text()

    expect(html.match(/<iframe /g)).toHaveLength(2)
    expect(html).toContain('a.md:3')
    expect(html).toContain('b.md:3')
  })

  it('serves the webframe shell', async () => {
    await write('a.md', fence('var x = 42;'))
    const { url } = await start()

    const response = await fetch(new URL('/frame', url))

    expect(response.status).toBe(200)
    expect(await response.text()).toContain('@webframe.ready')
  })

  it('serves the artifacts the frame will ask for', async () => {
    await write('a.md', fence('var x = 42;'))
    const { url } = await start()
    await fetch(url)

    const artifact = new URL(`/artifacts/github-dark/${snippetKey('var x = 42;')}.html`, url)
    const response = await fetch(artifact)

    expect(response.status).toBe(200)
    expect(await response.text()).toContain('var x = 42;')
  })

  it('picks up an edit on reload, re-rendering only what changed', async () => {
    await write('a.md', fence('var x = 42;'))
    const rendered: string[] = []
    const { url } = await start({
      render: async ({ code }) => {
        rendered.push(code)
        return `<div>${code}</div>`
      },
    })

    await fetch(url)
    expect(rendered).toEqual(['var x = 42;'])

    await write('a.md', fence('var x = 43;'))
    const html = await (await fetch(url)).text()

    expect(rendered).toEqual(['var x = 42;', 'var x = 43;'])
    expect(html.match(/<iframe /g)).toHaveLength(1)
  })

  it('shows the build failure instead of a blank page', async () => {
    await write('a.md', fence('var x = 42;'))
    const { url } = await start({
      render: async () => {
        throw new Error('glosharp not found')
      },
    })

    const html = await (await fetch(url)).text()

    expect(html).toContain('Could not render the snippets')
    expect(html).toContain('glosharp not found')
  })

  it('serves nothing from outside the artifacts directory', async () => {
    await write('a.md', fence('var x = 42;'))
    await write('secret.txt', 'top secret')
    const { url } = await start()

    const response = await fetch(new URL('/artifacts/%2e%2e%2fsecret.txt', url))

    expect(response.status).toBe(403)
  })

  it('advertises the address it actually bound', async () => {
    await write('a.md', fence('var x = 42;'))
    const { url } = await start({ host: '127.0.0.1' })

    // Advertising `localhost` while listening on 127.0.0.1 is unreachable
    // wherever `localhost` resolves to ::1 first.
    expect(new URL(url).hostname).toBe('127.0.0.1')
    expect((await fetch(url)).status).toBe(200)
  })

  it('404s anything else', async () => {
    await write('a.md', fence('var x = 42;'))
    const { url } = await start()

    expect((await fetch(new URL('/nope', url))).status).toBe(404)
  })

  it('skips rendering entirely with noBuild', async () => {
    await write('a.md', fence('var x = 42;'))
    let calls = 0
    const { url } = await start({
      noBuild: true,
      render: async () => {
        calls++
        return '<div/>'
      },
    })

    await fetch(url)

    expect(calls).toBe(0)
  })
})
