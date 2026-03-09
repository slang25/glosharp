## ADDED Requirements

### Requirement: Classify C# tokens using Roslyn Classifier
The `SyntaxClassifier` SHALL use Roslyn's `Classifier.GetClassifiedSpansAsync()` to produce classified spans for a given source text and compilation. Each span SHALL have a start position, length, and classification type.

#### Scenario: Classify keywords
- **WHEN** source `var x = 42;` is classified
- **THEN** `var` is classified as `keyword`

#### Scenario: Classify string literals
- **WHEN** source `var s = "hello";` is classified
- **THEN** `"hello"` is classified as `string`

#### Scenario: Classify type names semantically
- **WHEN** source `Console.WriteLine("hi");` is classified with a compilation that includes `System.Console`
- **THEN** `Console` is classified as a class name (not a plain identifier), because the Classifier uses semantic analysis

#### Scenario: Classify comments
- **WHEN** source contains `// this is a comment`
- **THEN** the comment text is classified as `comment`

### Requirement: Create AdhocWorkspace for classification
The classifier SHALL create an `AdhocWorkspace` with a `Document` containing the source text and the existing compilation's references to enable semantic classification.

#### Scenario: Workspace creation reuses compilation references
- **WHEN** a compilation has framework and NuGet package references
- **THEN** the workspace's project includes the same `MetadataReference` set so semantic classification resolves types correctly

### Requirement: Map Roslyn classification names to theme-friendly keys
The classifier SHALL map Roslyn's classification type strings (e.g., `"keyword"`, `"class name"`, `"string - verbatim"`, `"comment"`) to normalized theme keys (e.g., `keyword`, `className`, `string`, `comment`). Unrecognized classification types SHALL map to a `text` fallback key.

#### Scenario: Standard classification mapping
- **WHEN** Roslyn returns classification type `"class name"`
- **THEN** the mapped key is `className`

#### Scenario: Verbatim string classification
- **WHEN** Roslyn returns classification type `"string - verbatim"`
- **THEN** the mapped key is `string`

#### Scenario: Unknown classification type
- **WHEN** Roslyn returns an unrecognized classification type
- **THEN** the mapped key is `text`

### Requirement: Built-in github-dark theme
The system SHALL ship a `github-dark` theme record mapping classification keys to hex color values. At minimum, the theme SHALL define colors for: `keyword`, `className`, `structName`, `interfaceName`, `enumName`, `string`, `number`, `comment`, `operator`, `punctuation`, `localName`, `parameterName`, `methodName`, `propertyName`, `fieldName`, `namespaceName`, `text`, and `background`/`foreground` base colors.

#### Scenario: github-dark theme provides keyword color
- **WHEN** the `github-dark` theme is queried for `keyword`
- **THEN** it returns a hex color value (e.g., `#ff7b72`)

#### Scenario: github-dark theme provides background color
- **WHEN** the `github-dark` theme is queried for `background`
- **THEN** it returns a dark background color (e.g., `#0d1117`)

### Requirement: Built-in github-light theme
The system SHALL ship a `github-light` theme record with light-mode color values for the same classification keys as `github-dark`.

#### Scenario: github-light theme provides keyword color
- **WHEN** the `github-light` theme is queried for `keyword`
- **THEN** it returns a hex color value appropriate for light backgrounds (e.g., `#cf222e`)

#### Scenario: github-light theme provides background color
- **WHEN** the `github-light` theme is queried for `background`
- **THEN** it returns a light background color (e.g., `#ffffff`)

### Requirement: Theme provides popup colors
Each theme SHALL define popup-specific colors: `popupBackground`, `popupForeground`, `popupBorder`, `errorColor`, `highlightBackground`, `diffAddBackground`, `diffAddBorder`, `diffRemoveBackground`, `diffRemoveBorder`.

#### Scenario: Theme popup colors for dark theme
- **WHEN** the `github-dark` theme is queried for popup colors
- **THEN** it provides dark-appropriate values (e.g., `popupBackground: "#1e1e1e"`, `popupBorder: "#454545"`)

#### Scenario: Theme popup colors for light theme
- **WHEN** the `github-light` theme is queried for popup colors
- **THEN** it provides light-appropriate values (e.g., `popupBackground: "#f3f3f3"`, `popupBorder: "#c8c8c8"`)
