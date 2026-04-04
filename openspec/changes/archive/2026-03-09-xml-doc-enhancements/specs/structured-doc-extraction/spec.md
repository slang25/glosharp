## ADDED Requirements

### Requirement: GloSharpDocComment model
The system SHALL define a `GloSharpDocComment` class with the following fields: `summary` (string, nullable), `params` (list of name/text pairs), `returns` (string, nullable), `remarks` (string, nullable), `examples` (list of strings), and `exceptions` (list of type/text pairs). All collection fields SHALL default to empty lists.

#### Scenario: Doc comment with all tags
- **WHEN** an XML doc comment contains `<summary>`, `<param>`, `<returns>`, `<remarks>`, `<example>`, and `<exception>` elements
- **THEN** the resulting `GloSharpDocComment` has all fields populated with the extracted text

#### Scenario: Doc comment with only summary
- **WHEN** an XML doc comment contains only `<summary>`
- **THEN** the resulting `GloSharpDocComment` has `summary` set and all other fields empty/null

#### Scenario: No documentation
- **WHEN** a symbol has no XML documentation
- **THEN** `ExtractDocComment` returns null (not an empty `GloSharpDocComment`)

### Requirement: Extract summary text
The system SHALL extract the text content of the `<summary>` element, trimming leading and trailing whitespace.

#### Scenario: Simple summary
- **WHEN** XML contains `<summary>Gets the length of the string.</summary>`
- **THEN** `summary` is `"Gets the length of the string."`

#### Scenario: Multi-line summary
- **WHEN** XML contains a `<summary>` with newlines and indentation
- **THEN** `summary` is the content with internal whitespace normalized to single spaces and outer whitespace trimmed

### Requirement: Extract param tags
The system SHALL extract all `<param>` elements, preserving the `name` attribute and text content as a list of `GloSharpDocParam` objects with `name` and `text` fields.

#### Scenario: Multiple params
- **WHEN** XML contains `<param name="value">The value to write.</param>` and `<param name="format">The format string.</param>`
- **THEN** `params` contains two entries: `{ name: "value", text: "The value to write." }` and `{ name: "format", text: "The format string." }`

#### Scenario: No params
- **WHEN** XML contains no `<param>` elements
- **THEN** `params` is an empty list

### Requirement: Extract returns tag
The system SHALL extract the text content of the `<returns>` element.

#### Scenario: Returns present
- **WHEN** XML contains `<returns>True if the operation succeeded.</returns>`
- **THEN** `returns` is `"True if the operation succeeded."`

#### Scenario: No returns
- **WHEN** XML contains no `<returns>` element
- **THEN** `returns` is null

### Requirement: Extract remarks tag
The system SHALL extract the text content of the `<remarks>` element.

#### Scenario: Remarks present
- **WHEN** XML contains `<remarks>This method is thread-safe.</remarks>`
- **THEN** `remarks` is `"This method is thread-safe."`

### Requirement: Extract example tags
The system SHALL extract the text content of all `<example>` elements as a list of strings.

#### Scenario: Single example
- **WHEN** XML contains one `<example>var x = Foo();</example>`
- **THEN** `examples` contains one entry: `"var x = Foo();"`

#### Scenario: Multiple examples
- **WHEN** XML contains two `<example>` elements
- **THEN** `examples` contains two entries in document order

### Requirement: Extract exception tags
The system SHALL extract all `<exception>` elements, capturing the `cref` attribute as `type` and the text content as `text`, into a list of `GloSharpDocException` objects.

#### Scenario: Exception with cref
- **WHEN** XML contains `<exception cref="ArgumentNullException">Value cannot be null.</exception>`
- **THEN** `exceptions` contains `{ type: "ArgumentNullException", text: "Value cannot be null." }`

#### Scenario: Exception cref with fully qualified name
- **WHEN** XML contains `<exception cref="T:System.ArgumentNullException">...</exception>`
- **THEN** `type` is `"ArgumentNullException"` (the `T:` prefix and namespace are stripped)

### Requirement: Handle inline XML elements
The system SHALL render inline XML elements (`<see>`, `<paramref>`, `<c>`, `<seealso>`) as their inner text or relevant attribute value. Specifically: `<see cref="X"/>` becomes `"X"`, `<paramref name="x"/>` becomes `"x"`, `<c>code</c>` becomes `"code"`.

#### Scenario: Summary with see reference
- **WHEN** summary text is `Gets the <see cref="String"/> value.`
- **THEN** extracted summary is `"Gets the String value."`

#### Scenario: Param with paramref
- **WHEN** param text is `The same type as <paramref name="other"/>.`
- **THEN** extracted param text is `"The same type as other."`

### Requirement: Graceful handling of malformed XML
The system SHALL return null when XML documentation cannot be parsed, rather than throwing an exception.

#### Scenario: Invalid XML
- **WHEN** `GetDocumentationCommentXml()` returns malformed XML
- **THEN** `ExtractDocComment` returns null
