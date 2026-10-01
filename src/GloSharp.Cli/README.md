# GloSharp.Cli

`glosharp` is twoslash for C#: it compiles documentation snippets with Roslyn and emits the
hover, diagnostic and completion data that the Glo# renderers (Shiki, Expressive Code,
Docusaurus, GitBook) turn into interactive code blocks. It also renders standalone HTML and
verifies snippets in CI.

Source and documentation: https://github.com/slang25/glosharp

## Install

```sh
dotnet tool install --global GloSharp.Cli
# or, per repository
dotnet new tool-manifest && dotnet tool install GloSharp.Cli
```

The tool targets .NET 8 and rolls forward to any newer runtime (9, 10, 11, …), so it runs on
machines that only have a recent SDK.

## Commands

```sh
glosharp process snippet.cs                 # JSON for a file
echo 'var x = 1;' | glosharp process --stdin
glosharp render snippet.cs --standalone -o snippet.html
glosharp verify docs/ samples/extra.cs      # CI: fail on unexpected compile errors
glosharp init                               # write glosharp.config.json
glosharp compact-complog app.binlog -o app.glocontext
glosharp serve                              # long-running worker for build tools (see below)
```

Run `glosharp <command> --help` for every option. Common ones:

| Option | Meaning |
|---|---|
| `--framework <tfm>` | Target framework (default `net10.0`; with `--project`, the project's) |
| `--project <path>` | Compile against a project's packages, project references and its own build output |
| `--complog <path>` | Take references and options from a `.complog` or `.glocontext` |
| `--complog-project <name>` | `Name`, `Name.csproj` or `"Name (tfm)"` in a multi-project complog |
| `--region <name>` | Only the named `#region` (works with files and `--stdin`) |

Input comes from the file argument or `--stdin`. Without either, standard input is read only
when it is redirected, so an interactive `glosharp process` fails fast instead of waiting.

## Exit codes

`0` success (for `process`/`render`, JSON/HTML was produced; see `meta.compileSucceeded`),
`1` failure (including failed verification or no files to verify), `2` usage error
(unknown option, missing value, bad arguments).

## `verify` output

Errors use the MSBuild canonical format, which IDEs and GitHub's problem matchers (registered
by `actions/setup-dotnet`) turn into annotations:

```
/repo/docs/intro.cs(3,9): error CS0029: Cannot implicitly convert type 'string' to 'int' [/repo/docs/intro.cs]
```

Positions refer to the original file, so markers, `#:` directives and cut lines above an error
don't shift them. Errors in hidden (cut) setup code are reported at their real location too.
`GS1001` marks a file that could not be processed at all, `GS1002` a failed snippet with no
reportable error location, and `GS1003` (a warning, never a failure) a non-fatal problem such as a
`^?` caret that points past the end of its line or a `#:package` that couldn't be restored.

## `serve`

`glosharp serve` keeps one process (and its loaded compiler, reference assemblies and caches)
alive across many snippets, which is far faster than starting the CLI per snippet.
`@glosharp/core` uses it automatically; you only need the protocol to write your own client.

It speaks JSON lines over stdin/stdout (UTF-8, one object per line). The first line is a
handshake; check `protocol` before sending anything:

```json
{"type":"ready","protocol":1,"version":"0.1.0","commands":["process","render","ping"],"concurrency":8,"pid":4242}
```

Requests carry an `id` (number or string) that the response echoes. Responses come back in
completion order, not request order:

```json
{"id":1,"command":"process","code":"var x = 1;","options":{"framework":"net10.0","cwd":"/repo/docs"}}
{"id":1,"ok":true,"result":{ "code": "var x = 1;", "hovers": [ ... ], "meta": { ... } }}

{"id":2,"command":"render","options":{"file":"intro.cs","theme":"github-light"}}
{"id":2,"ok":true,"result":"<div class=\"glosharp-code\" ...>"}

{"id":3,"command":"process","code":"x","options":{"framework":"banana"}}
{"id":3,"ok":false,"error":{"kind":"usage","message":"...","exitCode":2,"stderr":"glosharp process: error: ..."}}
```

- `options` are the command-line options in camelCase: `file`, `framework`, `project`,
  `complog`, `complogProject`, `region`, `noRestore`, `cacheDir`, `config`, and for `render`,
  `theme` and `standalone`. Pass either `code` or `options.file`.
- `cwd` is the directory relative paths resolve against and where config discovery starts for
  `code` (default: the server's working directory). With `file`, discovery starts from the file's
  directory, as with `glosharp process <file>`.
- Results are exactly what `glosharp process` prints (on one line) and `glosharp render` writes.
  Errors carry the exit code and standard error the one-shot command would have produced. `kind`
  is `usage` (exit code 2), `failure` (1) or `protocol` (a malformed request; `id` is `null`
  when it couldn't be read).
- Up to `--concurrency` requests (default: the number of CPUs) run at once; more are queued.
- Nothing but protocol lines is written to stdout; logs (such as `dotnet restore` progress) go
  to stderr. At end of input the server answers the outstanding requests and exits with code 0.

## Environment

| Variable | Meaning |
|---|---|
| `DOTNET_ROOT` | .NET install to take reference assemblies from (default: the `dotnet` on `PATH`) |
| `GLOSHARP_CACHE_DIR` | Cache for downloaded targeting packs and file-based app restores |
| `GLOSHARP_PROCESS_TIMEOUT` | Timeout in seconds for child processes such as `dotnet restore` (default 300) |
