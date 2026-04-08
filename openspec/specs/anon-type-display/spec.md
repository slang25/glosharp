## ADDED Requirements

### Requirement: Detect anonymous types in hover symbols
The system SHALL detect anonymous types in hover symbol resolution by checking `INamedTypeSymbol.IsAnonymousType` on the symbol's type. Detection SHALL apply to local variables, parameters, properties, and method return types whose type is or contains an anonymous type.

#### Scenario: Local variable with anonymous type
- **WHEN** hover is extracted for `var x = new { Name = "test", Age = 42 };` targeting `x`
- **THEN** the system detects that the type of `x` is an anonymous type

#### Scenario: Array of anonymous types
- **WHEN** hover is extracted for `var items = new[] { new { Id = 1 } };` targeting `items`
- **THEN** the system detects that the array element type is an anonymous type

#### Scenario: LINQ projection producing anonymous type
- **WHEN** hover is extracted for `.Select(x => new { x.Name, Total = x.Price * x.Qty })` targeting the result variable
- **THEN** the system detects the anonymous type in the projected result

### Requirement: Replace anonymous type names with placeholders in display parts
The system SHALL replace compiler-generated anonymous type names in hover display parts with sequential placeholder labels using the format `'a`, `'b`, `'c`, etc. The same anonymous type within a single hover SHALL always receive the same placeholder letter. Placeholder assignment SHALL be scoped per-hover (not global across hovers).

#### Scenario: Single anonymous type placeholder
- **WHEN** hover is extracted for `var x = new { Name = "test" };` targeting `x`
- **THEN** the hover text shows `'a x` instead of the compiler-generated type name, and display parts contain the placeholder `'a` in place of the anonymous type name

#### Scenario: Multiple distinct anonymous types
- **WHEN** hover involves two distinct anonymous types (e.g., a tuple containing `new { Id = 1 }` and `new { Name = "x" }`)
- **THEN** the first anonymous type is assigned `'a` and the second is assigned `'b`

#### Scenario: Same anonymous type appears twice
- **WHEN** hover display parts reference the same anonymous type in multiple positions
- **THEN** both positions use the same placeholder letter

### Requirement: Generate type annotations for anonymous types
The system SHALL produce a `typeAnnotations` list on `GloSharpHover` containing one entry per distinct anonymous type detected in the hover. Each entry SHALL have a `name` field with the placeholder (e.g., `'a`) and an `expansion` field with the anonymous type's shape formatted as `new { Type1 Prop1, Type2 Prop2 }`. Property types SHALL use the same display format as the rest of the hover (short names, nullable annotations).

#### Scenario: Simple anonymous type annotation
- **WHEN** hover is extracted for `var x = new { Name = "test", Age = 42 };` targeting `x`
- **THEN** `typeAnnotations` contains `[{ name: "'a", expansion: "new { string Name, int Age }" }]`

#### Scenario: Anonymous type with array property
- **WHEN** hover is extracted for `var x = new { Name = "test", Readings = new[] { 1.0, 2.0 } };` targeting `x`
- **THEN** `typeAnnotations` contains `[{ name: "'a", expansion: "new { string Name, double[] Readings }" }]`

#### Scenario: Nested anonymous types
- **WHEN** hover is extracted for `var x = new { Name = "test", Details = new { Id = 1 } };` targeting `x`
- **THEN** `typeAnnotations` contains entries for both `'a` (outer type using `'b` for the nested property type) and `'b` (inner type), e.g., `[{ name: "'a", expansion: "new { string Name, 'b Details }" }, { name: "'b", expansion: "new { int Id }" }]`

#### Scenario: No anonymous types present
- **WHEN** hover is extracted for a symbol with no anonymous types (e.g., `int x = 42;`)
- **THEN** `typeAnnotations` is null/omitted from the output

### Requirement: Format anonymous type property access hovers
The system SHALL format hovers on property access of anonymous types to show the property type and anonymous type context. When hovering over a property name like `Name` on an anonymous type instance, the hover SHALL show the property signature with the anonymous type placeholder (e.g., `string 'a.Name { get; }`).

#### Scenario: Property access on anonymous type
- **WHEN** hover targets `Name` in `x.Name` where `x` is `new { Name = "test", Age = 42 }`
- **THEN** hover text shows `string 'a.Name { get; }` with `typeAnnotations` containing `'a is new { string Name, int Age }`

### Requirement: Format anonymous type constructor hovers
The system SHALL format hovers on the `new` keyword of anonymous type expressions to show the anonymous type's placeholder and shape.

#### Scenario: Hover on new keyword of anonymous type
- **WHEN** hover targets the `new` keyword in `new { Name = "test", Age = 42 }`
- **THEN** hover text shows `'a` (or similar identifier for the anonymous type) with `typeAnnotations` containing `'a is new { string Name, int Age }`
