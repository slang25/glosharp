# GloSharp

GloSharp compiles the C# snippets embedded in documentation and extracts what the compiler knows about them (hovers, diagnostics, completions) so docs sites can render IDE-quality, twoslash-style code, and so a snippet that stops compiling fails the docs build instead of reaching readers.

The product is written **GloSharp** in prose, identifiers and package names; **Glo#** is a display form for logos and site headers only.

## Language

### Snippets and markers

**Snippet**:
A self-contained piece of C# source, as written by an author, that GloSharp compiles and annotates as one unit.
_Avoid_: sample, example, block, fence body

**Code block**:
The container on a docs page (a Markdown fence, an Expressive Code frame) that holds a snippet.
_Avoid_: snippet (for the container), fence (except when talking about Markdown syntax)

**Marker**:
A line comment in GloSharp's own syntax inside a snippet that tells GloSharp what to show, hide, expect or emphasise. Marker lines are removed from what readers see.
_Avoid_: directive, annotation, magic comment, command

**Query**:
A marker whose caret points at a position on the code line above it and asks for information there: a hover query (`^?`) or a completion query (`^|`).
_Avoid_: pin, twoslash query

**Cut marker**:
A marker that hides part of a snippet from readers while keeping it in the compilation: everything above, everything below, or a delimited stretch.
_Avoid_: cut directive, above-hidden directive, hide marker

**Region**:
A named `#region` inside a larger source file, shown on its own as the snippet while the whole file is still compiled.
_Avoid_: section, excerpt, named snippet

**Hidden code**:
Lines of a snippet that are compiled but not shown to readers, because a cut marker or a region excludes them.
_Avoid_: setup code, invisible code

**Processed code**:
The snippet as readers see it, with markers, file-based app directives and hidden code removed. Every position in a result refers to processed code.
_Avoid_: output code, rendered code, display code, clean code

**Expected error**:
A diagnostic that an `@errors` marker declares for the next code line. It is still shown, and the snippet fails if it does not occur.
_Avoid_: allowed error, declared error

**Error suppression**:
A snippet-wide instruction that hides all diagnostics, or only listed codes, from the result. Unlike an expected error, it turns checking off for what it hides.
_Avoid_: ignored errors

**Highlight**:
A line-level emphasis on a processed line, with a kind: highlight, focus, add or remove.
_Avoid_: line marker, decoration, annotation

**Custom tag**:
An author-written note of kind log, warn, error or annotate, attached to the code line just before it.
_Avoid_: callout (that's how it renders), annotation, note

### Extracted information

**Result**:
Everything GloSharp reports for one snippet: processed code, hovers, diagnostics, completions, highlights, custom tags, hidden ranges and compilation facts. It is the contract between compiling and rendering.
_Avoid_: output, metadata, twoslash result

**Hover**:
The compiler's description of the symbol a token refers to: its signature, symbol kind, documentation and overload count. Every token that carries its own symbol has one.
_Avoid_: tooltip, quick info, type info, auto-hover

**Persistent hover**:
A hover requested by a hover query, shown beneath its line without any interaction.
_Avoid_: pinned hover, static hover, query result

**Diagnostic**:
A message from the compiler, or from GloSharp itself (GS codes), attached to a span of the snippet, with a code, a text and a severity (error, warning, info or hidden).
_Avoid_: error (for non-error severities), squiggle

**Unexpected error**:
An error-severity diagnostic, in visible or hidden code, that is neither expected nor suppressed. Any one of them makes the snippet fail.
_Avoid_: compile error, real error

**Processing warning**:
A non-fatal problem in processing a snippet, such as a failed package restore, a caret past the end of its line, or an empty completion list. Reported separately from diagnostics.
_Avoid_: meta warning, warning (unqualified)

**Completion**:
The list of items the compiler would offer at a completion query's caret, filtered by the characters already typed.
_Avoid_: IntelliSense, autocomplete, suggestions

**Verification**:
Checking a set of snippets and failing if any has an unexpected error or an expected error that did not occur. It is the CI gate that keeps documentation compiling.
_Avoid_: validation, lint

### Compilation context

**Compilation context**:
Everything a snippet is compiled against apart from the snippet itself: references, base compiler options, target framework and packages.
_Avoid_: environment, reference set, context (unqualified)

**Framework-only context**:
A compilation context made only of the target framework's reference assemblies. It is used when nothing else is provided.
_Avoid_: standalone mode, framework mode, bare snippet

**Project context**:
A compilation context taken from an existing, restored project's resolved packages.
_Avoid_: project mode, assets resolution

**File-based app directive**:
A `#:` line (`#:package`, `#:sdk`, `#:property`, `#:project`) that declares a snippet's own dependencies in the .NET SDK's file-based-app syntax. The SDK resolves them into the snippet's compilation context, and they are removed from processed code.
_Avoid_: marker, package marker, `@nuget`

**Complog**:
A compiler log captured from a real build, recording each of the build's compilations with their references and options.
_Avoid_: compilation log, build log, GloContext

**GloContext**:
A small, portable, reproducible compilation context compacted from a complog, keeping only what is needed for types, hovers and completions, and suitable for committing to a repository.
_Avoid_: compacted complog, compilation log, context file, artifact

**Pack pointer**:
A reference inside a GloContext recorded as a location in a NuGet targeting pack, verified by that pack's content hash, instead of embedded bytes. A GloContext with no pack pointers is self-contained.
_Avoid_: pointer reference, canonical reference

### Output and rendering

**Integration**:
A package that feeds GloSharp results into a docs toolchain's code rendering (Shiki, Expressive Code, GitBook).
_Avoid_: plugin, adapter, renderer

**Bridge**:
The Node-side layer that runs the GloSharp CLI and gives typed results to integrations.
_Avoid_: core, Node API, wrapper, client

**HTML renderer**:
GloSharp's own renderer, which turns a result into self-contained HTML that needs no script.
_Avoid_: standalone renderer, static renderer

**Fragment**:
The embeddable HTML for one rendered snippet. A standalone page is a fragment wrapped in a complete HTML document.
_Avoid_: partial, snippet HTML

**Popup**:
The on-screen form of a hover, opened when a reader points at or keyboard-focuses the hover's token.
_Avoid_: tooltip, hover card

**Callout**:
A rendered row below a code line that is never part of the copyable code: a diagnostic message, a persistent hover, a completion list or a custom tag.
_Avoid_: block content, line extras, message box

**Snippet key**:
The identity used to look up a snippet's result or artifact: a hash of the snippet's canonical form plus any options that change the result.
_Avoid_: code hash, content hash, cache key

**Artifact**:
A pre-rendered fragment for one snippet in one theme, published by CI under its snippet key so that a host which cannot compile, such as GitBook, can fetch it.
_Avoid_: rendered snippet; never use "artifact" for a GloContext
