# GitBook renders HTML that CI precomputes and publishes as static, content-addressed artifacts

GitBook's integration sandbox can't spawn processes or run Roslyn. Instead the docs repo's CI runs `glosharp render` on every `glosharp` fence and publishes `<theme>/<snippetKey>.html` to static hosting; a webframe hashes the fence body and fetches the matching artifact, falling back to plain code with a note when it's missing. There is no infrastructure to run, and CI and the browser only need to agree on the snippet text.

## Considered Options

- A hosted GloSharp API: rejected. It means operating a service that compiles arbitrary submitted C#, and enables nothing extra.
- Roslyn on .NET WASM in the frame: feasible, deferred. Too heavy per reader; maybe later as an editor preview.
- Looking artifacts up in the integration's `fetch` handler: rejected. It would make the integration an open proxy inlining arbitrary HTML on its own origin; the cost is that the artifacts host needs CORS.
- Claiming the `csharp` fence: rejected. It would hijack every C# block in the space and the snippets would stop being portable to other renderers.

## Consequences

- The key covers the code only, not fence attributes (GitBook may reformat props), so identical snippets requesting different frameworks fail the build rather than silently colliding.
- Content is stale until CI publishes; authors editing in GitBook see plain code.
- Inside the iframe, popups open downward and the frame grows while one is open. Growing upward would move the token out from under the pointer and make the frame oscillate.
