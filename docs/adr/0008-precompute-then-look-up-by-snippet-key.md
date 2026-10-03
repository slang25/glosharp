# Results are computed up front and looked up by a canonical snippet key

Shiki transformer hooks are synchronous and compiling is not. So unlike `@shikijs/twoslash`, every integration first processes all blocks asynchronously (batch or remark pass), then a synchronous transformer looks each block up by snippet key: SHA-256 of the canonical snippet (CRLF→LF, outer blank lines trimmed) plus any result-affecting options. There is exactly one canonicaliser, in `@glosharp/core`; the GitBook shell embeds its source text rather than reimplementing it, because two drifting definitions would silently miss every lookup.

## Consequences

- Canonicalisation is deliberately minimal (no interior whitespace normalisation), because that could change raw string literals.
- Changing the canonical form or key invalidates every published GitBook artifact.
