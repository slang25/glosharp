# `process` and `render` exit 0 on code that doesn't compile; only `verify` gates

Docs often show deliberately failing code, and the integrations need the result either way. `glosharp process`/`render` exit 0 whenever they produce output and report compile status in `meta.compileSucceeded`; exit 1 means GloSharp itself failed. Gating CI on unexpected errors is the job of `glosharp verify`, which honours `@errors`/`@noErrors`/`@suppressErrors`.
