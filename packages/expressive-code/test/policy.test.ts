import { afterEach, describe, expect, it } from 'vitest'
import { createEngine, recordStubArgs, resetStub, select, useStubResults } from './helpers.js'

afterEach(resetStub)

const unexpectedError = {
  line: 0, character: 12, length: 7,
  code: 'CS0029', message: "Cannot implicitly convert type 'string' to 'int'",
  severity: 'error', expected: false,
}

describe('CLI failures (onCliError)', () => {
  it('fails the build with install instructions when the CLI is missing', async () => {
    const { render } = createEngine({ executable: '/nonexistent/glosharp' })
    const rendering = render('var x = 42;', { file: `${process.cwd()}/src/pages/post.md`, blockIndex: 1 })
    await expect(rendering).rejects.toThrow(/dotnet tool install --global GloSharp\.Cli/)
    await expect(render('var x = 42;', { blockIndex: 1 })).rejects.toThrow(/code block 2 of 3 \(starting "var x = 42;"\)/)
    await expect(render('var x = 42;')).rejects.toThrow(/onCliError: 'warn'/)
  })

  it('names the document and block when the CLI crashes', async () => {
    useStubResults({ 'boom': { exit: 1, stderr: "Error: Region 'demo' not found in source file." } })
    const { render } = createEngine()
    const rendering = render('// boom', { file: `${process.cwd()}/docs/guide.md` })
    await expect(rendering).rejects.toThrow(/docs\/guide\.md, code block 1 of 3[\s\S]*Region 'demo' not found/)
    // Not an installation problem, so no install instructions
    await expect(rendering).rejects.not.toThrow(/dotnet tool install/)
  })

  it("with onCliError: 'warn', logs once and renders the block as plain code", async () => {
    const { render, warnings } = createEngine({ executable: '/nonexistent/glosharp', onCliError: 'warn' })
    const first = await render('var x = 42;\n//  ^?')
    const second = await render('var y = 1;', { blockIndex: 2 })

    expect(select(first.ast, '.glosharp-hover, .glosharp-static')).toHaveLength(0)
    expect(select(first.ast, '.ec-line')).toHaveLength(2) // markers left in place
    expect(select(second.ast, '.ec-line')).toHaveLength(1)

    expect(warnings).toHaveLength(2)
    expect(warnings[0]).toMatch(/could not process .*code block 1 of 3/)
    expect(warnings[0]).toMatch(/dotnet tool install/)
    expect(warnings[1]).toMatch(/skipped .*code block 3 of 3.*same CLI error/)
  })
})

describe('compile diagnostics (failOnErrors)', () => {
  it('logs unexpected errors with document, block and source line, and still renders them', async () => {
    useStubResults({
      'int count': {
        // The CLI removed the marker line, so rendered line 0 is source line 1
        code: 'int count = "three";\n',
        errors: [unexpectedError],
        meta: { compileSucceeded: false },
      },
    })
    const { render, warnings } = createEngine()
    const { ast } = await render('// @highlight\nint count = "three";', { file: `${process.cwd()}/src/pages/errors.md`, blockIndex: 2 })

    expect(select(ast, '.glosharp-error-message')).toHaveLength(1)
    expect(warnings).toHaveLength(1)
    expect(warnings[0]).toContain('src/pages/errors.md, code block 3 of 3 (starting "// @highlight")')
    expect(warnings[0]).toContain("line 2: CS0029: Cannot implicitly convert type 'string' to 'int'")
    expect(warnings[0]).toContain('failOnErrors')
  })

  it('prefers the CLI-provided sourceLine for locations', async () => {
    useStubResults({
      'int count': { errors: [{ ...unexpectedError, sourceLine: 6 }], meta: { compileSucceeded: false } },
    })
    const { render, warnings } = createEngine()
    await render('int count = "three";')
    expect(warnings[0]).toContain('line 7: CS0029')
  })

  it('fails the build on unexpected errors when failOnErrors is set', async () => {
    useStubResults({ 'int count': { errors: [unexpectedError], meta: { compileSucceeded: false } } })
    const { render } = createEngine({ failOnErrors: true })
    await expect(render('int count = "three";')).rejects.toThrow(/1 unexpected compile error:\n {2}line 1: CS0029/)
  })

  it('does not report expected errors, warnings or info diagnostics', async () => {
    useStubResults({
      'int count': {
        errors: [
          { ...unexpectedError, expected: true },
          { ...unexpectedError, code: 'CS0219', severity: 'warning', message: 'assigned but never used' },
          { ...unexpectedError, code: 'IDE0001', severity: 'info', message: 'simplify' },
        ],
      },
    })
    const { render, warnings } = createEngine({ failOnErrors: true })
    const { ast } = await render('int count = "three";')
    expect(warnings).toEqual([])
    expect(select(ast, '.glosharp-error-message')).toHaveLength(3)
  })

  it('reports errors in hidden code (hiddenErrors) and fails on them with failOnErrors', async () => {
    const hiddenResult = {
      'Helper': {
        code: 'Helper();\n',
        hiddenErrors: [{ ...unexpectedError, line: 0, sourceLine: 1, code: 'CS0246', message: "The type or namespace name 'Foo' could not be found" }],
        meta: { compileSucceeded: false },
      },
    }
    useStubResults(hiddenResult)
    const lenient = createEngine()
    const { ast } = await lenient.render('// ---cut-start---\nFoo f = null;\n// ---cut-end---\nHelper();')
    expect(select(ast, '.glosharp-error-message')).toHaveLength(0) // can't be placed on the page
    expect(lenient.warnings).toHaveLength(1)
    expect(lenient.warnings[0]).toContain('line 2 (in hidden code, not shown on the page): CS0246')

    const strict = createEngine({ failOnErrors: true })
    await expect(strict.render('// ---cut-start---\nFoo f = null;\n// ---cut-end---\nHelper();')).rejects.toThrow(/CS0246/)
  })

  it('logs meta.warnings from the CLI', async () => {
    useStubResults({
      'Newtonsoft': { meta: { warnings: ["Package restore failed for 'Does.Not.Exist@1.0.0'"] } },
    })
    const { render, warnings } = createEngine()
    await render('#:package Newtonsoft.Json@13.0.3')
    expect(warnings).toEqual([expect.stringMatching(/code block 1 of 3.*: Package restore failed for 'Does\.Not\.Exist@1\.0\.0'/)])
  })
})

describe('per-block opt-in / opt-out', () => {
  it('processes every C# block by default and skips `no-glosharp` / `glosharp=false` blocks', async () => {
    const calls = recordStubArgs()
    const { render } = createEngine()
    await render('var a = 1;')
    await render('var b = 1;', { language: 'cs' })
    await render('var c = 1;', { language: 'c#' })
    await render('var d = 1;', { meta: 'title="x.cs" no-glosharp' })
    await render('var e = 1;', { meta: 'glosharp=false' })
    await render('const f = 1', { language: 'js' })
    expect(calls()).toHaveLength(3)
  })

  it('with explicitTrigger, only processes blocks marked `glosharp`', async () => {
    const calls = recordStubArgs()
    useStubResults({ 'var b': { errors: [unexpectedError] } })
    const { render, warnings } = createEngine({ explicitTrigger: true })
    const plain = await render('var a = 1;\n//  ^?')
    const marked = await render('var b = 1;', { meta: 'glosharp {1}' })
    expect(calls()).toHaveLength(1)
    expect(select(plain.ast, '.ec-line')).toHaveLength(2) // untouched
    expect(select(marked.ast, '.glosharp-error-message')).toHaveLength(1)
    expect(warnings).toHaveLength(1)
  })

  it('does not run the CLI for skipped blocks even when it is missing', async () => {
    const { render } = createEngine({ executable: '/nonexistent/glosharp' })
    await expect(render('var x = 1;', { meta: 'no-glosharp' })).resolves.toBeDefined()
  })
})

describe('region', () => {
  it('passes the region option to the CLI, and a block meta `region` overrides it', async () => {
    const calls = recordStubArgs()
    const { render } = createEngine({ region: 'getting-started', project: './Demo.csproj' })
    await render('#region getting-started\nvar a = 1;\n#endregion')
    await render('#region other\nvar b = 1;\n#endregion', { meta: 'region="other"' })
    const [first, second] = calls()
    expect(first).toEqual(expect.arrayContaining(['--stdin', '--region', 'getting-started', '--project', './Demo.csproj']))
    expect(second).toEqual(expect.arrayContaining(['--stdin', '--region', 'other']))
  })
})
