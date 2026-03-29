## MODIFIED Requirements

### Requirement: Theme-aware styling via styleSettings
The plugin SHALL define theme-aware styles for popup colors (background, foreground, border), error colors (underline, message background), warning colors (underline, message background), and info colors (underline, message background) that adapt to EC's light/dark theme switching. These styles SHALL be embedded in `baseStyles` using CSS custom properties. The plugin SHALL NOT return a `styleSettings` property on the plugin object, as `baseStyles` is the sole mechanism for theme-aware styling.

#### Scenario: Dark theme popup styling
- **WHEN** the EC instance uses a dark theme
- **THEN** popup elements use the dark theme color variables defined in baseStyles

#### Scenario: Light theme popup styling
- **WHEN** the EC instance uses a light theme
- **THEN** popup elements use the light theme color variables

#### Scenario: Plugin object has no styleSettings property
- **WHEN** `pluginTwohash()` is called
- **THEN** the returned plugin object does not contain a `styleSettings` property

#### Scenario: Plugin works with EC 0.41 without workaround
- **WHEN** `pluginTwohash()` is added directly to an expressive-code `plugins` array in EC 0.41+
- **THEN** the plugin registers without errors and no consumer-side property stripping is needed
