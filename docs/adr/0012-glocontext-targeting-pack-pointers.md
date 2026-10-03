# `.glocontext` v2 points at canonical nuget.org targeting packs instead of embedding framework references

Framework reference assemblies are ~95% of a context and are published immutably on nuget.org, so v2 replaces them with `{pack, path}` pointers (collapsing to a single `packAll` entry when a compilation covers a pack's whole `ref/<tfm>/`). Each pack carries one content hash verified by the reader before use. The bytes recorded are the nuget.org packs, not the producer's installed SDK packs, which are re-signed and sometimes different builds. The compactor matches each reference to its canonical file by raw hash, then MVID, then file name plus version, and only references whose origin path lies inside a targeting pack are eligible, so a package DLL that happens to be called `System.Runtime.dll` can't be swapped for framework bytes.

## Consequences

- Reading may need the pack: it's found in the NuGet global packages folder, the glosharp cache, the installed SDK (read-only, hash-verified), or downloaded from nuget.org. Offline consumers without it fail with a remedy, or the producer can write v1 with `--self-contained`.
- Unmatched or unobtainable pack references are embedded with a warning rather than failing.
- NuGet library references are still embedded; pointing at them would need a guarantee that Refasmer is deterministic on the consumer side.
