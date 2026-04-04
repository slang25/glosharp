### Requirement: Implicit usings from config replace built-in defaults
The system SHALL read the `implicitUsings` string array from `glosharp.config.json`. When present, these SHALL **replace** the built-in default global usings entirely. Each entry SHALL be prepended as a `global using <namespace>;` directive before compilation. These usings SHALL NOT appear in the output code.

#### Scenario: Config replaces default implicit usings
- **WHEN** `glosharp.config.json` contains `{"implicitUsings": ["System.Text", "System.Text.Json"]}`
- **THEN** compilation includes only `global using System.Text;` and `global using System.Text.Json;` as implicit usings (built-in defaults like System.Linq are NOT included)

#### Scenario: Config with full custom set
- **WHEN** `glosharp.config.json` contains `{"implicitUsings": ["System", "System.Linq", "System.Text.Json"]}`
- **THEN** compilation includes exactly those three as global usings and no others

#### Scenario: Empty array removes all implicit usings
- **WHEN** `glosharp.config.json` contains `{"implicitUsings": []}`
- **THEN** no global usings are prepended before compilation

### Requirement: Missing implicitUsings uses built-in defaults
The system SHALL use the built-in default global usings when `implicitUsings` is not present in the config.

#### Scenario: No implicitUsings property
- **WHEN** `glosharp.config.json` contains `{"framework": "net9.0"}` with no `implicitUsings`
- **THEN** the built-in default global usings (System, System.Collections.Generic, System.IO, System.Linq, System.Net.Http, System.Threading, System.Threading.Tasks) are used

#### Scenario: No config file at all
- **WHEN** no `glosharp.config.json` exists
- **THEN** the built-in default global usings are used
