## ADDED Requirements

### Requirement: Extract named region from source file
The system SHALL support extracting a named `#region` block from a C# source file. When a region name is specified, only the code within that region SHALL appear in the output `code`, but the full file SHALL be compiled for accurate type resolution.

#### Scenario: Extract a named region
- **WHEN** a source file contains `#region getting-started` ... `#endregion` and region name `getting-started` is requested
- **THEN** the output `code` contains only the lines between `#region` and `#endregion` (exclusive of the directives themselves), and hover/error positions are relative to the extracted region

#### Scenario: Region with markers inside
- **WHEN** the named region contains `^?` hover markers
- **THEN** the markers are processed normally and hover positions reference the extracted region's line numbers

#### Scenario: Region with surrounding context
- **WHEN** code outside the region defines types or using directives needed by code inside the region
- **THEN** compilation succeeds because the full file is compiled, and hovers inside the region resolve correctly

### Requirement: Region extraction is a hidden-line mask
Region extraction SHALL be text-based (it works for files and `--stdin`) and SHALL hide lines rather than rewrite the source: every line outside the region, the region's own `#region`/`#endregion` lines and nested region directives inside it are hidden from `code`, while the whole file — including all region directives — is still compiled. Cut markers (`---cut---`, `---cut-start---`/`---cut-end---`, ...) apply in addition, inside and outside the region.

#### Scenario: Cut inside the region
- **WHEN** the region contains a `---cut-start---`/`---cut-end---` block
- **THEN** that block is hidden too and does not appear in `code`

#### Scenario: Cut outside the region does not leak
- **WHEN** code before the region contains a balanced `---cut-start---`/`---cut-end---` block
- **THEN** only the region's own lines appear in `code`

#### Scenario: Errors outside the region
- **WHEN** code outside the region does not compile
- **THEN** the errors are reported in `hiddenErrors` and `compileSucceeded` is false (see json-output)

### Requirement: Nested regions are matched by depth
`#region`/`#endregion` pairs SHALL be matched with a depth counter, so an inner `#endregion` does not close the requested outer region. Region names SHALL match exactly (`demo` does not match `#region demo-long`).

#### Scenario: Outer region containing an inner region
- **WHEN** region `outer` contains `#region inner` ... `#endregion` followed by more code and its own `#endregion`
- **THEN** `code` contains all of `outer`'s code lines, including the inner region's content, but no `#region`/`#endregion` lines

#### Scenario: Unclosed region
- **WHEN** the requested region has no matching `#endregion`
- **THEN** it extends to the end of the file

### Requirement: Hide region directives from output
The `#region` and `#endregion` lines SHALL be excluded from the output `code`.

#### Scenario: Region directives not in output
- **WHEN** a file with `#region`/`#endregion` blocks is processed (with or without `--region`)
- **THEN** the `#region` and `#endregion` lines do not appear in the output `code`

### Requirement: Error on missing region
The system SHALL report an error when the requested region name is not found in the source file.

#### Scenario: Region not found
- **WHEN** region name `nonexistent` is requested but the file has no matching `#region nonexistent`
- **THEN** the system fails with an error message indicating the region was not found

### Requirement: First match wins for duplicate region names
When multiple regions share the same name, the system SHALL use the first matching region.

#### Scenario: Duplicate region names
- **WHEN** a file contains two `#region setup` blocks and region `setup` is requested
- **THEN** the output contains the content of the first matching region
