## ADDED Requirements

### Requirement: Render command produces HTML output
The CLI SHALL accept a `render` command with the same file/stdin input and options as `process` (`--framework`, `--project`, `--region`, `--no-restore`, `--cache-dir`), plus additional rendering options. The `render` command SHALL process the source through the core, classify tokens, and write HTML to stdout.

#### Scenario: Render a file to stdout
- **WHEN** `glosharp render src/Example.cs` is run
- **THEN** the CLI processes the file, classifies tokens, and writes self-contained HTML to stdout

#### Scenario: Render from stdin
- **WHEN** `echo "var x = 42;" | glosharp render --stdin` is run
- **THEN** the CLI reads from stdin, processes, classifies, and writes HTML to stdout

#### Scenario: Render with project context
- **WHEN** `glosharp render snippet.cs --project ./MyProject.csproj` is run
- **THEN** the CLI resolves NuGet packages from the project and includes accurate type information in hovers

#### Scenario: Render with region extraction
- **WHEN** `glosharp render src/Example.cs --region getting-started` is run
- **THEN** the CLI extracts the named region and renders only that region's code

### Requirement: Theme option for render command
The CLI SHALL accept a `--theme` option on the `render` command specifying the color theme. Valid values SHALL be `github-dark` and `github-light`. The default SHALL be `github-dark`.

#### Scenario: Render with explicit theme
- **WHEN** `glosharp render file.cs --theme github-light` is run
- **THEN** the HTML uses github-light color scheme

#### Scenario: Render with default theme
- **WHEN** `glosharp render file.cs` is run without `--theme`
- **THEN** the HTML uses github-dark color scheme

#### Scenario: Invalid theme name
- **WHEN** `glosharp render file.cs --theme nonexistent` is run
- **THEN** the CLI exits with non-zero code and writes an error to stderr listing valid theme names

### Requirement: Standalone option for render command
The CLI SHALL accept a `--standalone` flag on the `render` command. When specified, the output SHALL be a complete HTML page instead of an embeddable fragment.

#### Scenario: Standalone HTML page
- **WHEN** `glosharp render file.cs --standalone` is run
- **THEN** the output is a complete HTML page with `<!DOCTYPE html>`, `<head>`, and `<body>`

#### Scenario: Fragment output (default)
- **WHEN** `glosharp render file.cs` is run without `--standalone`
- **THEN** the output is an embeddable `<div>` fragment

### Requirement: Output option for render command
The CLI SHALL accept an `--output` option on the `render` command specifying a file path to write the HTML to instead of stdout.

#### Scenario: Write to file
- **WHEN** `glosharp render file.cs --output rendered.html` is run
- **THEN** the HTML is written to `rendered.html` instead of stdout

#### Scenario: Write to stdout (default)
- **WHEN** `glosharp render file.cs` is run without `--output`
- **THEN** the HTML is written to stdout

### Requirement: Render command exit codes
The `render` command SHALL follow the same exit code conventions as `process`: exit 0 on success, non-zero on unexpected compilation errors.

#### Scenario: Successful render exits 0
- **WHEN** source compiles cleanly
- **THEN** the CLI exits with code 0

#### Scenario: Compilation error exits non-zero
- **WHEN** source has unexpected compilation errors
- **THEN** the CLI exits with non-zero code (HTML with error annotations is still written to stdout)

### Requirement: Render command diagnostic output to stderr
The `render` command SHALL write only HTML to stdout. Progress messages, warnings, and errors SHALL go to stderr.

#### Scenario: Clean stdout for render
- **WHEN** the CLI renders successfully
- **THEN** stdout contains only HTML, and any diagnostics go to stderr
