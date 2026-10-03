# The Roslyn core runs as a .NET CLI; JS integrations talk to it only through JSON

Roslyn needs .NET, but the doc renderers (Shiki, Expressive Code, GitBook) are Node. All compiler work stays in GloSharp.Core behind the `glosharp` CLI. The npm packages spawn it and consume only its JSON (or rendered HTML), so the `GloSharpResult` JSON is the contract and each integration is a thin adapter over it.

## Considered Options

- Roslyn on .NET WASM, in-process in Node: feasible (Roslyn is pure IL) but costs ~15–30 MB and seconds to first compile, for nothing a spawned process can't do. Only revisit where spawning is impossible (see [ADR-0013](0013-gitbook-ci-precomputed-artifacts.md)).
- A native Node addon hosting .NET: fragile cross-platform builds for no gain.

## Consequences

- The npm packages don't ship the .NET tool; users install `GloSharp.Cli` as a dotnet tool and the bridge locates it.
- Spawning per snippet turned out expensive (.NET startup, MEF, reference loading), so the bridge keeps a pool of long-lived `glosharp serve` workers speaking the same contract, falling back to one-shot invocation for CLIs without `serve`.
- The CLI and npm packages release in lockstep and the bridge warns on mismatch, because a CLI from another release line can silently drop JSON fields.
