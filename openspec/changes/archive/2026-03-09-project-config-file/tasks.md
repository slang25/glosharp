## 1. Config Model & Loader

- [x] 1.1 Create `TwohashConfig` record in `TwoHash.Core` with properties: `Framework`, `Project`, `CacheDir`, `NoRestore`, and nested `RenderConfig` (`Theme`, `Standalone`). All nullable/optional.
- [x] 1.2 Create `ConfigLoader` class with `LoadAsync(string? explicitPath, string startDirectory)` that deserializes `twohash.config.json` using `System.Text.Json` with case-insensitive property matching and unknown property ignoring.
- [x] 1.3 Implement directory walk-up discovery in `ConfigLoader`: starting from `startDirectory`, check each parent for `twohash.config.json` until found or root reached.
- [x] 1.4 Implement relative path resolution: resolve `Project` and `CacheDir` paths relative to the config file's directory, not CWD.
- [x] 1.5 Add error handling: return null when no config found, throw with clear message on invalid JSON, throw when explicit path doesn't exist.

## 2. CLI Integration

- [x] 2.1 Add `--config <path>` argument parsing to `process`, `verify`, and `render` commands in `Program.cs`.
- [x] 2.2 Wire config loading into `process` command: call `ConfigLoader` after argument parsing, merge config defaults with CLI args (CLI wins), pass merged options to `TwohashProcessor`.
- [x] 2.3 Wire config loading into `verify` command with same merge logic.
- [x] 2.4 Wire config loading into `render` command, including `render.theme` and `render.standalone` config values.
- [x] 2.5 Implement `init` subcommand: write default `twohash.config.json` to CWD, refuse if exists unless `--force`, print explanation to stderr.

## 3. Node Bridge

- [x] 3.1 Add `configFile?: string` to `TwohashOptions` and `TwohashProcessOptions` interfaces in `types.ts`.
- [x] 3.2 Update `createTwohash()` in `twohash.ts` to pass `--config` flag to CLI when `configFile` is set (instance-level or per-call, per-call wins).

## 4. Tests

- [x] 4.1 Test `ConfigLoader` discovery: config in same dir, parent dir, no config found, stdin/CWD fallback.
- [x] 4.2 Test config parsing: full config, partial config, empty config, unknown properties ignored, invalid JSON error.
- [x] 4.3 Test merge precedence: CLI arg overrides config value, config provides default when CLI omits.
- [x] 4.4 Test relative path resolution: project and cacheDir paths resolve relative to config file location.
- [x] 4.5 Test `--config` flag: explicit path used, auto-discovery skipped, error on missing file.
- [x] 4.6 Test `init` command: creates file, refuses overwrite, `--force` overwrites, output is valid JSON.
