// Reader-side performance of the gallery pages: what loading, hovering and
// scrolling GloSharp output costs the browser's main thread. Not part of the
// test suite (numbers vary by machine); run it before and after a rendering
// change and compare.
//
//   npm run gallery:build && npm run perf -- [options]
//
//   --pages a,b      gallery pages (default: shiki-dark,ec-dark,standalone-dark)
//   --scale n        repeat each page's cases n times, like a long docs page (default 4)
//   --cpu n          CPU throttling factor (default 4, a mid-range laptop or phone)
//   --cdp <url>      attach to a running Chrome (--remote-debugging-port) instead of
//                    launching Playwright's Chromium; a desktop Chrome has a real GPU
//   --json <file>    also write the results as JSON
//
// Times are main-thread milliseconds from a Chrome trace. Hover drives the real
// pointer onto 40 tokens; "worst event" is the slowest pointer event's duration
// (Event Timing, the measure behind INP). Scroll wheels through the page with
// the pointer resting over the code, so hover state changes as blocks pass by.
import { createServer, type Server } from 'node:http'
import { readFileSync, writeFileSync } from 'node:fs'
import { join, resolve, normalize, extname } from 'node:path'
import { chromium, type Browser, type CDPSession, type Page } from '@playwright/test'

const DIST = resolve(import.meta.dirname!, '../gallery-dist')
const args = process.argv.slice(2)
const arg = (name: string, fallback: string) => {
  const i = args.indexOf(`--${name}`)
  return i >= 0 && args[i + 1] ? args[i + 1] : fallback
}
const PAGES = arg('pages', 'shiki-dark,ec-dark,standalone-dark').split(',')
const SCALE = Number(arg('scale', '4'))
const CPU = Number(arg('cpu', '4'))
const CDP = arg('cdp', '')
const JSON_OUT = arg('json', '')
const HOVERS = 40

const TRACE_CATEGORIES = 'devtools.timeline,disabled-by-default-devtools.timeline,loading,toplevel'
const PHASES = ['ParseHTML', 'UpdateLayoutTree', 'Layout', 'Paint', 'FunctionCall'] as const

/** A gallery page with its cases repeated `SCALE` times. */
function scaledPage(name: string): string {
  const html = readFileSync(join(DIST, `${name}.html`), 'utf-8')
  const first = html.indexOf('<section class="case"')
  const end = html.lastIndexOf('</section>') + '</section>'.length
  if (first < 0) return html
  return html.slice(0, first) + html.slice(first, end).repeat(SCALE) + html.slice(end)
}

function serve(): Promise<{ server: Server; base: string }> {
  const types: Record<string, string> = { '.html': 'text/html', '.css': 'text/css', '.js': 'text/javascript', '.woff2': 'font/woff2' }
  const server = createServer((req, res) => {
    const path = normalize(decodeURIComponent(new URL(req.url ?? '/', 'http://x').pathname)).replace(/^(\.\.[/\\])+/, '')
    try {
      const page = /^\/perf-(.+)\.html$/.exec(path)
      const body = page ? scaledPage(page[1]) : readFileSync(join(DIST, path))
      res.writeHead(200, { 'content-type': types[extname(path)] ?? 'application/octet-stream' })
      res.end(body)
    } catch {
      res.writeHead(404).end()
    }
  })
  return new Promise(ok => server.listen(0, '127.0.0.1', () => {
    const address = server.address()
    ok({ server, base: `http://127.0.0.1:${typeof address === 'object' && address ? address.port : 0}` })
  }))
}

type TraceEvent = { name: string; ph: string; pid: number; tid: number; ts: number; dur?: number; args?: { name?: string } }

/** Main-thread busy time and per-phase totals (ms) for whatever `action` does. */
async function traced(cdp: CDPSession, action: () => Promise<void>) {
  const events: TraceEvent[] = []
  const collect = (data: { value: object[] }) => { events.push(...(data.value as TraceEvent[])) }
  cdp.on('Tracing.dataCollected', collect)
  await cdp.send('Tracing.start', { categories: TRACE_CATEGORIES, transferMode: 'ReportEvents' })
  await action()
  const done = new Promise(ok => cdp.once('Tracing.tracingComplete', ok))
  await cdp.send('Tracing.end')
  await done
  cdp.off('Tracing.dataCollected', collect)

  const mains = new Set(events.filter(e => e.name === 'thread_name' && e.args?.name === 'CrRendererMain').map(e => `${e.pid}:${e.tid}`))
  const totals: Record<string, number> = { busy: 0 }
  for (const e of events) {
    if (e.ph !== 'X' || !e.dur || !mains.has(`${e.pid}:${e.tid}`)) continue
    if (e.name === 'RunTask') totals.busy += e.dur
    if ((PHASES as readonly string[]).includes(e.name)) totals[e.name] = (totals[e.name] ?? 0) + e.dur
  }
  return Object.fromEntries(Object.entries(totals).map(([k, v]) => [k, Math.round(v / 1000)]))
}

async function measure(page: Page, cdp: CDPSession, url: string) {
  await page.goto('about:blank')
  let stats = { nodes: 0, kb: 0, dcl: 0 }
  const load = await traced(cdp, async () => {
    await page.goto(url, { waitUntil: 'load' })
    await page.waitForTimeout(1000)
    stats = await page.evaluate(() => {
      const nav = performance.getEntriesByType('navigation')[0] as PerformanceNavigationTiming
      return { nodes: document.getElementsByTagName('*').length, kb: Math.round(nav.encodedBodySize / 1024), dcl: Math.round(nav.domContentLoadedEventEnd) }
    })
  })

  await page.evaluate(() => {
    const w = window as unknown as { __events: number[][] }
    w.__events = []
    new PerformanceObserver(list => {
      for (const e of list.getEntries()) if (/pointer|mouse/.test(e.name)) w.__events.push([e.startTime, e.duration])
    }).observe({ type: 'event', durationThreshold: 16 } as PerformanceObserverInit)
  })
  const tokens = await page.locator('.glosharp-hover:not(.glosharp-hover-persistent)').count()
  const step = Math.max(1, Math.floor(tokens / HOVERS))
  const hover = await traced(cdp, async () => {
    for (let i = 0; i < tokens && i / step < HOVERS; i += step) {
      const point = await page.evaluate((i) => {
        const el = document.querySelectorAll('.glosharp-hover:not(.glosharp-hover-persistent)')[i]
        el.scrollIntoView({ block: 'center' })
        const r = el.getClientRects()[0] ?? el.getBoundingClientRect()
        return { x: r.left + Math.min(r.width / 2, 4), y: r.top + r.height / 2 }
      }, i)
      await page.mouse.move(5, 5)
      await page.waitForTimeout(150)
      await page.mouse.move(point.x, point.y)
      await page.waitForTimeout(250)
    }
  })
  const worstEvent = await page.evaluate(() => {
    const events = (window as unknown as { __events: number[][] }).__events
    return Math.max(0, ...events.map(([, duration]) => duration))
  })

  await page.evaluate(() => window.scrollTo(0, 0))
  await page.mouse.move(400, 430)
  let frames = { slow: 0, total: 0 }
  const scroll = await traced(cdp, async () => {
    await page.evaluate(() => {
      const w = window as unknown as { __frames: number[]; __raf: number }
      w.__frames = []
      const tick = (t: number) => { w.__frames.push(t); w.__raf = requestAnimationFrame(tick) }
      w.__raf = requestAnimationFrame(tick)
    })
    const height = await page.evaluate(() => document.documentElement.scrollHeight)
    for (let y = 0; y < height && y < 24000; y += 120) {
      await page.mouse.wheel(0, 120)
      await page.waitForTimeout(16)
    }
    await page.waitForTimeout(300)
    frames = await page.evaluate(() => {
      const w = window as unknown as { __frames: number[]; __raf: number }
      cancelAnimationFrame(w.__raf)
      const gaps = w.__frames.slice(1).map((t, i) => t - w.__frames[i])
      return { slow: gaps.filter(g => g > 33.4).length, total: gaps.length }
    })
  })

  return { ...stats, load, hover: { ...hover, worstEvent }, scroll: { ...scroll, slowFrames: frames.slow, frames: frames.total } }
}

const { server, base } = await serve()
let browser: Browser | undefined
const results: Record<string, unknown> = {}
try {
  browser = CDP ? await chromium.connectOverCDP(CDP) : await chromium.launch()
  const context = CDP ? browser.contexts()[0] : await browser.newContext()
  for (const name of PAGES) {
    const page = await context.newPage()
    await page.setViewportSize({ width: 1280, height: 860 })
    const cdp = await context.newCDPSession(page)
    await cdp.send('Emulation.setCPUThrottlingRate', { rate: CPU })
    console.error(`measuring ${name}…`)
    results[name] = await measure(page, cdp, `${base}/perf-${name}.html`)
    await page.close()
  }
} finally {
  if (CDP) await browser?.close().catch(() => {}) // disconnects; leaves the user's Chrome running
  else await browser?.close()
  server.close()
}

console.log(`${SCALE}x cases, ${CPU}x CPU throttling, main-thread ms\n`)
for (const [name, r] of Object.entries(results) as [string, Awaited<ReturnType<typeof measure>>][]) {
  console.log(`${name}: ${r.nodes} elements, ${r.kb} KB, DOMContentLoaded ${r.dcl} ms`)
  console.log(`  load    ${JSON.stringify(r.load)}`)
  console.log(`  hover   ${JSON.stringify(r.hover)}`)
  console.log(`  scroll  ${JSON.stringify(r.scroll)}`)
}
if (JSON_OUT) writeFileSync(JSON_OUT, JSON.stringify({ scale: SCALE, cpu: CPU, results }, null, 2))
