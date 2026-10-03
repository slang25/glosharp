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

## CI

- `.github/workflows/ci.yml` runs on every PR: .NET tests on Linux, Windows and macOS; package
  tests on Linux and Windows; snippet verification, website and example builds; the Playwright
  suite. It also fails if the package versions drift from the release version (below).
- The website deploys from `main` only after CI passes.

## Releases

Glo# ships six packages that release together, at one version: `GloSharp.Cli` and
`GloSharp.Core` on NuGet, and `@glosharp/core`, `@glosharp/shiki`, `@glosharp/expressive-code`
and `@glosharp/gitbook` on npm. The npm packages run the CLI and read its JSON, so they're only
tested against the CLI from the same commit.

### The version

The version is set in one place, `<Version>` in [`src/Directory.Build.props`](src/Directory.Build.props).
The two .NET projects (and `glosharp --version`) read it from there. Everything else is stamped from
it by a script:

```sh
npm run version:set 0.1.0-alpha.3   # or 0.1.0, 0.2.0-beta.1, ...
npm run version:check               # what CI runs
```

`version:set` writes the version to `src/Directory.Build.props`, the `version` of each
`packages/*/package.json`, their exact `@glosharp/*` dependency pins, the matching
`package-lock.json` entries, and `packages/glosharp/src/version.generated.ts`. That last file is
the CLI version `@glosharp/core` expects. Don't edit any of these by hand; `version:check` (in CI)
fails if they disagree.

`@glosharp/core` runs `glosharp --version` once per process. If the CLI it finds is from a
different release line, it prints one warning saying which CLI to install. A different release
line means a different `major.minor`, or a different prerelease id (`alpha`, `beta`, `rc`).
CLIs built from source in CI report `0.0.0-ci.*`, and those aren't checked.
`GLOSHARP_SKIP_VERSION_CHECK=1` turns the check off.

### Cutting a release

Releases are cut by a tag. `.github/workflows/release.yml` runs when a `v*` tag is pushed:

1. **Prepare.** Checks that the tag is exactly `v<Version>`, that all the versions agree, and that
   the tagged commit is on `main`.
2. **CI.** Runs the whole of `ci.yml`.
3. **Pack.** Packs both nupkgs and installs the tool from them. It checks that `glosharp --version`
   reports the release version and that a snippet compiles. Then it packs the four npm tarballs,
   checks their versions and internal pins, installs all four into a scratch project and renders a
   hover through `@glosharp/core`. These exact files become the release artifacts.
4. **NuGet.** Pushes `GloSharp.Core` and then `GloSharp.Cli` (`--skip-duplicate`).
5. **npm.** Publishes core, shiki, expressive-code and gitbook in that order, with
   `--provenance`. Prereleases get a dist-tag named after their prerelease id (`0.2.0-beta.1` →
   `beta`); stable versions get `latest`. Until a stable version exists, `latest` is moved to each
   new prerelease too, so a bare `npm install @glosharp/shiki` doesn't stay on an old build.
   Versions that are already published are skipped.
6. **GitHub Release.** Creates the release with generated notes, marks it as a prerelease when it
   is one, and attaches the nupkgs and tarballs.

To cut a prerelease:

```sh
git switch main && git pull
git switch -c release/0.1.0-alpha.3
npm run version:set 0.1.0-alpha.3
git commit -am "Release 0.1.0-alpha.3"
# open a PR, let CI pass, merge it, then tag the merged commit on main:
git switch main && git pull
git tag v0.1.0-alpha.3
git push origin v0.1.0-alpha.3
```

To promote to stable, do the same with the stable version: `npm run version:set 0.1.0`, merge,
then tag `v0.1.0`. That publishes to npm's `latest` and creates a regular (non-prerelease) GitHub
Release. Prerelease and stable builds are built separately; nothing gets re-tagged.

**Dry run.** Run the `Release` workflow by hand (Actions → Release → Run workflow) from any
branch. `dry-run` is ticked by default. It runs everything up to and including the pack and smoke
tests, uploads the would-be artifacts, and publishes nothing. When run from a branch it is always a
dry run.

**A release that failed part-way.** Use "Re-run failed jobs" on the run, which reuses the packed
artifacts. Every publish step skips what is already on the registry, so a re-run only finishes the
release. If the run can't be re-run, dispatch `Release` on the tag (pick the tag under "Use
workflow from") and untick `dry-run`. Never reuse a version that's already been published:
registries don't allow a version to be replaced, so bump the version and cut a new tag.

### Repository settings and secrets

Set these up once, before the first release:

- **Environment `release`** (Settings → Environments). Both publish jobs run in it. Add required
  reviewers if releases should wait for approval, and limit it to `v*` tags.
- **NuGet trusted publishing.** On nuget.org, add a trusted publishing policy for owner
  `slang25`, repository `glosharp`, workflow file **`release.yml`**, environment `release`.
  Policies are tied to a workflow file, so a policy for the old `publish-nuget.yml` no longer
  matches. Then add the repository secret **`NUGET_USER`**: the nuget.org account name that owns
  the policy. `NuGet/login` exchanges the job's OIDC token for a short-lived API key, so no
  long-lived NuGet API key is stored.
- **npm trusted publishing.** For each of the four packages on npmjs.com (Settings → Trusted
  publishing → GitHub Actions), set organization/user `slang25`, repository `glosharp`, workflow
  **`release.yml`** and environment `release`. `@glosharp/gitbook` has never been published, and
  trusted publishing can only be configured for a package that already exists. For its first
  publish, either publish it once by hand or set `NPM_TOKEN` (below). Trusted publishing needs
  npm 11.5.1 or later; the workflow upgrades npm when it has to.
- **`NPM_TOKEN`** (optional): a granular npm token with publish rights on the `@glosharp` scope.
  It's used as a fallback for packages without trusted publishing, and for moving the `latest`
  dist-tag while there's no stable release (dist-tag changes can't use trusted publishing). If it
  isn't set, the release still succeeds and the run shows the `npm dist-tag add` commands to run by
  hand. Remove it once trusted publishing is set up and a stable release exists.
- The `GITHUB_TOKEN` creates the GitHub Release; the workflow requests `contents: write` for that
  job only.

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
