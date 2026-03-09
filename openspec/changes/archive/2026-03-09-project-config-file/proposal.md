## Why

Every twohash CLI invocation currently requires repeating flags like `--framework net9.0 --project ./MyProject.csproj --cache-dir .twohash-cache`. For documentation projects with dozens of snippets, this creates verbose build scripts and fragile configurations where a missed flag silently produces different results. A project-level config file eliminates repetition and makes twohash settings discoverable and version-controllable.

## What Changes

- Add support for a `twohash.config.json` config file that provides project-wide defaults for all CLI commands (`process`, `verify`, `render`).
- Config file is auto-discovered by walking up from the input file/directory to find the nearest `twohash.config.json`.
- CLI arguments override config file values (explicit flags always win).
- The Node bridge accepts a `configFile` option and also supports auto-discovery.
- A new `twohash init` command scaffolds a `twohash.config.json` with commented defaults.

## Capabilities

### New Capabilities
- `config-file-loading`: Parsing `twohash.config.json`, walking up directories to find it, merging with CLI args (CLI wins), and validating the config schema.
- `cli-init-command`: The `twohash init` command that scaffolds a default `twohash.config.json` file.

### Modified Capabilities
- `cli-tool`: CLI commands gain config file awareness — auto-discovery of `twohash.config.json` and a `--config` flag to specify an explicit path. New `init` subcommand.
- `node-bridge`: `TwohashOptions` gains a `configFile` option and auto-discovery support, so the Node bridge respects project config without extra per-call flags.

## Impact

- **C# core**: New `ConfigLoader` class in `TwoHash.Core` for parsing and merging config.
- **CLI**: All three commands (`process`, `verify`, `render`) gain config awareness. New `init` command added.
- **Node bridge**: `TwohashOptions` type extended. Config resolution logic added before CLI invocation.
- **No breaking changes**: All existing CLI invocations continue to work identically — config file is purely additive.
