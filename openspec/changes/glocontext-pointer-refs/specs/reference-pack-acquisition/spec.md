## ADDED Requirements

### Requirement: Ordered pack source chain
`GloSharp.Core` SHALL provide a `ReferencePackResolver` that locates a targeting pack `{id, version}` by consulting an ordered chain of sources: (1) the NuGet global packages folder (`NUGET_PACKAGES` environment variable, else `<user home>/.nuget/packages`), (2) the glosharp pack cache (`GLOSHARP_CACHE_DIR` environment variable, else `<LocalApplicationData>/glosharp/packs`), (3) download from nuget.org. The installed SDK `packs/` directory SHALL NOT be used as a source because its bytes are not canonical. The chain SHALL be injectable so tests can supply directory-backed sources without network access.

#### Scenario: Global packages folder hit
- **WHEN** the requested pack version exists under the NuGet global packages folder
- **THEN** the resolver returns files from there and performs no network access

#### Scenario: Cache hit after prior download
- **WHEN** the pack is absent from the global packages folder but present in the glosharp cache
- **THEN** the resolver returns files from the cache and performs no network access

#### Scenario: Sources are injectable
- **WHEN** a test constructs the resolver with a custom source list
- **THEN** only the supplied sources are consulted, in order

### Requirement: nuget.org download populates the glosharp cache
When earlier sources miss, the resolver SHALL download `https://api.nuget.org/v3-flatcontainer/{id}/{version}/{id}.{version}.nupkg` (id and version lowercased), extract only the `ref/**/*.dll` entries into the glosharp cache at `<cache>/<id>/<version>/…` preserving relative paths, and serve from the cache thereafter. Partial downloads SHALL NOT be visible in the cache (extract to a temp directory, then atomic rename).

#### Scenario: Successful download
- **WHEN** the pack is not available locally and the network is reachable
- **THEN** the nupkg is downloaded, its ref DLLs land in the glosharp cache, and the requested files are returned

#### Scenario: Interrupted download leaves no partial cache entry
- **WHEN** a download or extraction fails midway
- **THEN** the cache contains no directory for that pack version and a retry starts clean

#### Scenario: Download failure is reported with the URL
- **WHEN** nuget.org returns a non-success status or is unreachable
- **THEN** the resolver surfaces an error naming the pack id, version, and attempted URL

### Requirement: Acquisition failures enumerate the search
When no source can supply the pack, the resolver SHALL throw an exception naming the pack id and version and every location consulted (paths and, when attempted, the download URL).

#### Scenario: All sources miss offline
- **WHEN** the pack is in no local source and the network is unavailable
- **THEN** the exception lists the global packages folder path, the glosharp cache path, and the nuget.org URL that could not be reached
