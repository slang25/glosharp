## Context

Twohash CLI currently requires all options (`--framework`, `--project`, `--cache-dir`, `--no-restore`) to be specified on every invocation. For documentation projects with many snippets sharing the same project context, this means build scripts repeat the same flags dozens of times. There's no mechanism for project-wide defaults.

The ROADMAP identifies this as the next high-impact feature. The existing CLI uses manual argument parsing in `Program.cs`, and options flow through `TwohashProcessorOptions` to the core. The Node bridge has `TwohashOptions` (instance-level) and `TwohashProcessOptions` (per-call), both of which would benefit from config file awareness.

## Goals / Non-Goals

**Goals:**
- Provide a `twohash.config.json` file format for project-wide defaults
- Auto-discover config by walking up from the working file/directory
- Allow CLI args to override any config value
- Support all existing CLI options as config properties: `framework`, `project`, `cacheDir`, `noRestore`, and render-specific options (`theme`, `standalone`)
- Add `--config` flag for explicit config file path
- Add `twohash init` command to scaffold a config file
- Extend the Node bridge to support config file resolution

**Non-Goals:**
- Per-file or per-directory overrides (e.g., nested config files that merge) — single nearest config wins
- Config file format alternatives (YAML, TOML, `.twohashrc`) — JSON only for simplicity and tooling support
- IDE integration (VS Code settings sync) — out of scope
- Config file schema validation beyond basic type checking — keep it simple

## Decisions

### 1. File format: JSON (`twohash.config.json`)

**Alternatives considered:**
- `.twohashrc` (INI-style or JSON) — less discoverable, no standard extension for tooling
- `twohash.config.yaml` — adds a YAML parser dependency to the .NET project
- Section in `package.json` — only works in Node.js projects, not standalone .NET

**Decision:** `twohash.config.json`. JSON is natively supported by `System.Text.Json` in .NET and trivially parsed in Node.js. The explicit `.json` extension gives editors syntax highlighting and validation support. The `twohash.config.json` name follows the pattern of `tsconfig.json`, `jest.config.json`, etc.

### 2. Discovery: walk up from input path

The CLI will search for `twohash.config.json` starting from the directory of the input file (or the input directory for `verify`) and walking up parent directories until one is found or the filesystem root is reached.

**Alternatives considered:**
- Only look in CWD — too restrictive for monorepos
- Look in CWD then walk up — ambiguous which directory is "CWD" during build tool invocations

**Decision:** Walk up from the input file's directory. This matches `tsconfig.json` behavior and works naturally in monorepos where the config lives at the repo root.

For `--stdin` mode: walk up from the current working directory, since there's no input file path.

### 3. Merge strategy: CLI wins, flat merge

Config values provide defaults. CLI arguments override them completely (no partial merging of nested objects). This is a simple, predictable model.

```
effective_value = cli_arg ?? config_value ?? built_in_default
```

### 4. Config schema

```json
{
  "framework": "net9.0",
  "project": "./samples/Samples.csproj",
  "cacheDir": ".twohash-cache",
  "noRestore": false,
  "render": {
    "theme": "github-dark",
    "standalone": false
  }
}
```

All properties are optional. The `render` section groups render-specific options to avoid polluting the top level. Unknown properties are ignored (forward compatibility).

### 5. Implementation location: `TwoHash.Core` with `ConfigLoader`

A new `ConfigLoader` class in `TwoHash.Core` handles:
- File discovery (walk-up search)
- JSON deserialization into a `TwohashConfig` record
- Merging with `TwohashProcessorOptions`

This keeps the CLI thin — it calls `ConfigLoader`, merges with CLI args, and passes the result to the processor.

### 6. Node bridge: `configFile` option + auto-discovery

The Node bridge gains:
- `configFile?: string` in `TwohashOptions` — explicit config path passed as `--config` to CLI
- Auto-discovery: if no `configFile` is set, the CLI handles discovery itself (no Node-side file walking)

This keeps the Node bridge thin — config resolution stays in the .NET CLI.

### 7. `twohash init` command

Writes a `twohash.config.json` to the current directory with all properties commented out (using JSON with `//` comments is not valid, so instead we write all properties with their default values and a note). Actually, since JSON doesn't support comments, we'll output a valid JSON file with all properties set to their defaults, and print a message to stderr explaining each property.

**Alternative:** Output a file with `//` comments (JSONC) — but standard `System.Text.Json` and most tools expect valid JSON.

**Decision:** Output valid JSON with defaults. Print a human-readable explanation to stderr.

## Risks / Trade-offs

- **[Risk] Config file not found silently** → By design: missing config is fine, all options have defaults. The `--config` flag errors if the specified file doesn't exist.
- **[Risk] Confusion about merge precedence** → Mitigated by simple rule: CLI always wins. Document clearly.
- **[Risk] Breaking change if config file accidentally picked up** → Low risk since all config values match existing CLI defaults. A config file only changes behavior if it explicitly sets non-default values.
- **[Trade-off] No nested/inherited config** → Keeps the model simple. Users who need per-directory overrides can use CLI flags in their build scripts. Can be added later if demand emerges.
- **[Trade-off] JSON-only (no YAML/TOML)** → Reduces maintenance burden. JSON is universally supported. Can add alternatives later if needed.
