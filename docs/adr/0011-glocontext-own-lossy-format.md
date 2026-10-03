# Committable compilation contexts are our own `.glocontext` format, not a slimmed complog

A raw complog runs to tens of MB, mostly IL bodies, analyzers and sources GloSharp never reads. `compact-complog` writes `.glocontext`: a versioned header (magic `GLOCTX`, version, flags, reserved baseline slots) followed by a zstd (long mode) tar of a JSON manifest and content-hash-deduplicated reference blobs. References are run through Refasmer; analyzers, original sources and generator output are dropped. Output is deterministic so regeneration gives clean git diffs. `--complog` accepts either format, distinguished by magic bytes.

## Considered Options

- Keep the output a valid `.complog`: rejected. Once stripped, other complog tools would crash or mislead; owning the format is more honest.
- zstd trained dictionaries: measured, no gain.
- `--patch-from` baselines: works, but shipping and versioning baselines isn't worth ~1 MB. The header slots are reserved for it.

## Consequences

- The format is lossy: it can't rebuild the project, and analyzer diagnostics never appear in docs (they never did; complogs are read with analyzers off).
- ZstdSharp.Port is a runtime dependency until System.IO.Compression gains zstd; the swap is isolated behind `IZstdCodec` with no format change.
