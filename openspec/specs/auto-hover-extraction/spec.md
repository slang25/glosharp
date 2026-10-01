# auto-hover-extraction Specification

## Purpose
Produce hover information for every meaningful token, with `^?` queries marked persistent.

## Requirements
### Requirement: Extract hovers for all semantically meaningful tokens
The system SHALL walk all descendant tokens in the syntax tree and extract hover data for tokens on an allow-list that carry their own symbol: identifier tokens (including contextual keywords such as `var`), predefined type keywords (`int`, `string`, ...), `this`/`base`, and the `new` of target-typed (`new()`) and anonymous object creation. Every other token — operators, punctuation, literals and statement keywords — SHALL be skipped; such tokens SHALL NOT borrow the hover of an enclosing call, declaration or member access.

A token's symbol SHALL be resolved from its own syntax node via `GetSymbolInfo()` (falling back to the first candidate symbol) or `GetDeclaredSymbol()`. The only parent walk allowed is from a name inside `NameEquals`/`NameColon` to the node it labels (anonymous type members, named tuple elements). `this`/`base` show the type they refer to.

#### Scenario: Auto-hover on local variable
- **WHEN** source contains `var x = 42;` with no `^?` marker
- **THEN** the output contains a hover for token `x` with text `(local variable) int x`

#### Scenario: Auto-hover on method call
- **WHEN** source contains `Console.WriteLine("hello");` with no `^?` marker
- **THEN** the output contains hovers for `Console` and `WriteLine` tokens

#### Scenario: No hover for punctuation
- **WHEN** source contains `var x = 42;`
- **THEN** the output does NOT contain hovers for `;`, `=`, or whitespace tokens

#### Scenario: No hover for string literals
- **WHEN** source contains `Console.WriteLine("hello");`
- **THEN** the output does NOT contain a hover for the `"hello"` literal

#### Scenario: Hover for inferred var keyword
- **WHEN** source contains `var x = 42;` and `var` resolves to `int`
- **THEN** the output contains a hover for the `var` token showing the inferred type

#### Scenario: No hover for case keyword
- **WHEN** source contains `switch (x) { case 1: break; }` inside a method
- **THEN** the output does NOT contain a hover for the `case` token

#### Scenario: No hover for break keyword
- **WHEN** source contains `switch (x) { case 1: break; }` inside a method
- **THEN** the output does NOT contain a hover for the `break` token

#### Scenario: No hover for return keyword
- **WHEN** source contains `return x;` inside a method
- **THEN** the output does NOT contain a hover for the `return` token

#### Scenario: No hover for if/else keywords
- **WHEN** source contains `if (true) { } else { }` inside a method
- **THEN** the output does NOT contain hovers for the `if` or `else` tokens

#### Scenario: No hover for switch keyword
- **WHEN** source contains `switch (x) { }` inside a method
- **THEN** the output does NOT contain a hover for the `switch` token

#### Scenario: Hover preserved for predefined type keywords
- **WHEN** source contains `int x = 42;`
- **THEN** the output contains a hover for the `int` token showing `struct System.Int32`

#### Scenario: Hover preserved for string type keyword
- **WHEN** source contains `string s = "hello";`
- **THEN** the output contains a hover for the `string` token showing `class System.String`

### Requirement: Auto-extracted hovers are non-persistent by default
All hovers extracted via automatic token walking SHALL have `persistent` set to `false`.

#### Scenario: Auto-hover persistence flag
- **WHEN** source contains `var x = 42;` with no `^?` marker
- **THEN** the hover for `x` has `persistent: false`

### Requirement: Map auto-hover positions to processed code
Auto-extracted hover positions SHALL be mapped from compilation-code positions back to processed-code line numbers using the existing line offset map. Hovers for tokens in hidden sections (before `---cut---`/`---cut-before---`, after `---cut-after---`, or within `---cut-start---`/`---cut-end---`) SHALL be excluded.

#### Scenario: Position mapping after marker removal
- **WHEN** source has a `// @highlight` marker on line 1 followed by `var x = 42;` on line 2
- **THEN** the auto-hover for `x` references line 1 in the processed code (after marker removal)

#### Scenario: Hidden code excluded from auto-hovers
- **WHEN** source has setup code before `// ---cut---` followed by display code
- **THEN** only tokens in the display code produce auto-hovers

### Requirement: Deduplicate auto-hovers with persistent hovers
When a token has both an auto-extracted hover and a `^?`-triggered persistent hover at the same position, the system SHALL emit only the persistent hover (not both).

#### Scenario: Persistent hover takes precedence
- **WHEN** source contains `var x = 42;` followed by `//  ^?` targeting `x`
- **THEN** the output contains exactly one hover for `x` with `persistent: true`

#### Scenario: No hover for operators inside a call
- **WHEN** source contains `Console.WriteLine(1 + 2);` and `var c = a * b;`
- **THEN** there are no hovers for `+` or `*` (previously they showed the enclosing `WriteLine` and `c`)

#### Scenario: Unresolved identifier does not borrow its declarator
- **WHEN** source contains `int total = missing;` (with `@noErrors`)
- **THEN** there is no hover for `missing` (previously it showed `total`)

### Requirement: LINQ range variables
Range variables SHALL be displayed as `(range variable) <type> <name>` with `symbolKind` `"Local"`, both at their declaration (`from p in people`) and at every usage. The type comes from the usage's type info, or for an unused declaration from the clause (`from`/`join` element type, `let` expression type).

#### Scenario: Range variable declaration
- **WHEN** source contains `from p in people` where `people` is `(string, int)[]` and a `^?` marker targets `p`
- **THEN** the hover text is `(range variable) (string, int) p` (previously the enclosing variable, or `? p`)
