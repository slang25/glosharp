# Contributing to Glo#

## Prerequisites

- [.NET SDK](https://dotnet.microsoft.com/download) **10.0** or later (`global.json` asks for
  10.0.100+ and rolls forward to newer SDKs), plus the **.NET 8 runtime**: the CLI and tests
  target `net8.0`.
- [Node.js](https://nodejs.org/) **22.18** or later (the test scripts run TypeScript directly).
  CI uses Node 24.

## Repository layout

| Path | What |
| --- | --- |
| `src/GloSharp.Core` | The Roslyn engine: marker parsing, compilation, hover/completion extraction, HTML renderer |
| `src/GloSharp.Cli` | The `glosharp` dotnet tool |
| `tests/GloSharp.Tests` | C# tests (TUnit) |
| `packages/glosharp` | `@glosharp/core`, the Node bridge that runs the CLI |
| `packages/shiki`, `packages/expressive-code`, `packages/gitbook` | The integrations |
| `tests/e2e` | CLI → JSON → HTML end-to-end tests (needs .NET) |
| `tests/rendering` | Browser rendering suite: committed fixtures, gallery, Playwright (no .NET) |
| `samples/` | Curated snippets; the source of the rendering fixtures |
| `examples/`, `website/` | Example sites and the project website |
| `openspec/` | Specs and change proposals |

## Build

```sh
npm ci                      # install all workspaces
npm run build               # build the @glosharp/* packages
dotnet build                # build the CLI, core and tests (GloSharp.slnx)
```

## Run the CLI from source

```sh
dotnet run --project src/GloSharp.Cli -- process samples/local-variables.cs
```

For the npm packages, examples and website, build the CLI once and point them at it with
`GLOSHARP_EXECUTABLE` (the examples pass it as the `executable` option):

```sh
npm run cli:build           # dotnet build src/GloSharp.Cli -c Release
export GLOSHARP_EXECUTABLE="$PWD/src/GloSharp.Cli/bin/Release/net8.0/GloSharp.Cli"
#   Windows: ...\bin\Release\net8.0\GloSharp.Cli.exe
```

<!-- TODO(merge): if the CLI's target framework changes, update the net8.0 path segment above
     and in examples/README.md. -->

Without it, the bridge looks for `glosharp` on `PATH`. To test the packaged tool the way users
install it (this is what CI does):

```sh
dotnet pack src/GloSharp.Cli -c Release -o artifacts/nupkg -p:Version=0.0.0-dev.1
dotnet tool install GloSharp.Cli --tool-path artifacts/tool --add-source artifacts/nupkg --version 0.0.0-dev.1
artifacts/tool/glosharp --version
```

Bump the `-dev.N` suffix on each re-pack: NuGet caches packages by version, so re-installing the
same version picks up the old build.

## Test

### C# (TUnit)

```sh
dotnet restore tests/GloSharp.Tests/fixtures/sample-project/SampleProject.csproj   # once
dotnet test
```

`global.json` opts `dotnet test` into Microsoft.Testing.Platform, which TUnit needs on the
.NET 10+ SDK; the old VSTest mode fails there. You can also run the test project directly, which
accepts TUnit's own options:

```sh
dotnet run --project tests/GloSharp.Tests/ -- --treenode-filter "/*/*/MarkerParserTests/*"
```

The file-based app tests (`#:package`, `#:sdk`) need the .NET 10+ SDK.

### npm packages

```sh
npm test                    # vitest in every package under packages/
npx vitest run              # or, inside one package directory
```

Some package tests run the real CLI, so put `glosharp` on `PATH` first (for example the
tool-path install above: `export PATH="$PWD/artifacts/tool:$PATH"`).

### End-to-end

```sh
npm test -w tests/e2e       # builds and runs the CLI via dotnet
```

### Snippets and docs

```sh
npm run verify:docs         # glosharp verify over samples/ and website/src/examples/
```

Every snippet in `samples/`, `website/src/examples/` and the example sites must compile, or
declare its errors with `// @errors:`. Don't use `// @noErrors` to make a snippet pass: it
suppresses errors, so the snippet is no longer checked.

### Rendering (browser)

Any change to how hovers, errors or popups render goes through the
[rendering feedback loop](tests/rendering/README.md):

```sh
npm run fixtures:update -w tests/rendering   # after a CLI or samples/ change (needs .NET)
npm run gallery:build -w tests/rendering && npm run gallery:serve -w tests/rendering
npx playwright install chromium firefox      # once, in tests/rendering
npm test -w tests/rendering                  # Playwright invariants
```

CI fails when the committed fixtures drift from the CLI's output (`fixtures:check`), so commit the
regenerated fixtures with the change that caused them.

### Examples and website

```sh
npm run build -w website
npm run build -w examples/astro-blog         # also examples/expressive-code, examples/docusaurus-docs
npm run render -w examples/standalone
```

CI builds all of them and fails if the output contains no hovers.

## CI and releases

- `.github/workflows/ci.yml` runs on every PR: .NET tests on Linux, Windows and macOS; package
  tests on Linux and Windows; snippet verification, website and example builds; the Playwright
  suite.
- The website deploys from `main` only after CI passes.
- `Publish NuGet packages` and `Publish npm packages` are manual (`workflow_dispatch`). Both run CI
  first. Leave `version` empty to publish the next `alpha.N`, or give the same explicit version to
  both so the CLI and the npm packages match.

## OpenSpec workflow

Features are specified in `openspec/` before they're built:

- `openspec/specs/<capability>/spec.md` is the current behaviour, as requirements with scenarios.
- A change lives in `openspec/changes/<name>/` (proposal, design, tasks, and delta specs). The
  `/opsx:propose`, `/opsx:apply` and `/opsx:archive` commands in `.claude/commands/` drive it, or
  use the [`openspec`](https://github.com/Fission-AI/OpenSpec) CLI directly.
- When a change is done, archive it: `openspec archive <name>` merges its delta specs into
  `openspec/specs/` and moves it to `openspec/changes/archive/`.
- `openspec validate --specs` checks the spec files.

## Commits and pull requests

Keep commits focused, and run the tests for the areas you touched. Update the README or package
README when you change user-facing behaviour, and `design/data-format.md` when you change the JSON
output.
