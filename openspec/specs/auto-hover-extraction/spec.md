## ADDED Requirements

### Requirement: Extract hovers for all semantically meaningful tokens
The system SHALL walk all descendant tokens in the syntax tree and extract hover data for every token that resolves to a symbol via `GetSymbolInfo()` or `GetDeclaredSymbol()`. Tokens that do not resolve to any symbol SHALL be skipped. When a symbol is resolved only via the parent-walk fallback (walking up parent nodes to find a declared symbol), the system SHALL discard it if the token is a C# keyword, since keyword tokens have no meaningful symbol of their own and the parent-walk produces misleading hover info (e.g., the containing method).

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
