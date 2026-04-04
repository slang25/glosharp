## ADDED Requirements

### Requirement: Render completion lists in HAST
The `root` hook SHALL inject completion list elements into the HAST tree for each completion result. Completion lists SHALL be rendered as a styled dropdown-like element positioned at the query location.

#### Scenario: Completion list rendered
- **WHEN** the glosharp result contains completions at line 2, character 8
- **THEN** the HAST tree contains a `<ul class="glosharp-completion-list">` element positioned near the query location, with `<li>` elements for each completion item

#### Scenario: Completion item display
- **WHEN** a completion item has `label: "WriteLine"`, `kind: "Method"`, `detail: "void Console.WriteLine(string?)"`
- **THEN** the rendered `<li>` includes the label text, a kind icon/badge via CSS class `glosharp-completion-kind-Method`, and the detail text

#### Scenario: No completions renders nothing
- **WHEN** the glosharp result has an empty completions array
- **THEN** no completion list elements are injected into the HAST

### Requirement: Detect completion markers for processing
The marker detection logic SHALL recognize `^|` markers in addition to `^?` markers when deciding whether to invoke glosharp processing on a code block.

#### Scenario: Block with only completion markers
- **WHEN** a csharp code block contains `^|` markers but no `^?` markers
- **THEN** the transformer invokes glosharp processing on the block
