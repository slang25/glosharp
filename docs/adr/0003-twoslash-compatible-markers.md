# Marker syntax follows twoslash; C# additions only where twoslash has no equivalent

Authors who know TypeScript twoslash should be able to read a GloSharp snippet. Where twoslash has a marker we use its exact spelling (`^?`, `^|`, `// @errors:`, `// @noErrors`, the `---cut-*---` family, `@log`/`@warn`/`@error`/`@annotate`); our earlier `@above-hidden`/`@hide`/`@show` were dropped in favour of the twoslash cut markers. C#-only needs get `// @` markers (`@langVersion`, `@nullable`, `@suppressErrors`). Packages are declared with the SDK's own file-based-app syntax (`#:package`, `#:sdk`), not a GloSharp marker.

## Considered Options

- `// @nuget:` markers with our own package resolution: rejected once .NET 10 shipped `#:package`, which the SDK already resolves.
- `#:property LangVersion=…` for compiler settings: rejected. `#:` lines feed MSBuild and only make sense for file-based apps, whereas language version and nullable context are Roslyn settings GloSharp applies in every compilation mode.
