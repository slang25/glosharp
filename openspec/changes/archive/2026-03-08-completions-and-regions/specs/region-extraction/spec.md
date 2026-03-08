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
