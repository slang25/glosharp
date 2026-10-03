# References come from artifacts the SDK already produced; GloSharp never evaluates MSBuild or resolves NuGet

A snippet's references come from exactly one source, in fixed priority order: a complog/`.glocontext`, a project's `project.assets.json`, a file-based app's `#:` directives, or the framework reference packs alone. `project.assets.json` is read directly as JSON. For `#:` snippets the SDK is asked to *restore* (never build), so a snippet that deliberately doesn't compile still gets its packages. A complog is authoritative: its references and compiler options are used to compile the snippet in a fresh compilation; the project's compilation is never reused and nothing is added on top.

## Considered Options

- MSBuild API / MSBuildLocator: heavyweight, slow, and coupled to the SDK version.
- NuGet client libraries: a large dependency tree duplicating what restore already did.
- `dotnet build` of file-based apps: slow, and fails on snippets that intentionally don't compile.
