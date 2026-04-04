# NuGet package resolution for Roslyn compilation

This is the hardest problem in glosharp. Roslyn needs actual DLL references to produce accurate type information. NuGet packages must be resolved to assemblies on disk.

## The problem

Given C# code like:

```csharp
using Newtonsoft.Json;

var json = JsonConvert.SerializeObject(new { Name = "test" });
```

Roslyn needs `Newtonsoft.Json.dll` as a `MetadataReference` to resolve `JsonConvert`. Without it, `GetSymbolInfo()` returns null and we get no hover information.

## Where assemblies live

### Framework assemblies

The .NET SDK ships reference assemblies:

```
~/.dotnet/packs/Microsoft.NETCore.App.Ref/{version}/ref/{tfm}/
```

Example:
```
~/.dotnet/packs/Microsoft.NETCore.App.Ref/9.0.0/ref/net9.0/System.Runtime.dll
~/.dotnet/packs/Microsoft.NETCore.App.Ref/9.0.0/ref/net9.0/System.Console.dll
~/.dotnet/packs/Microsoft.NETCore.App.Ref/9.0.0/ref/net9.0/System.Linq.dll
```

These are always available if the SDK is installed.

### NuGet package assemblies

After `dotnet restore`, packages are cached in:

```
~/.nuget/packages/{package-id}/{version}/lib/{tfm}/{assembly}.dll
```

Example:
```
~/.nuget/packages/newtonsoft.json/13.0.3/lib/net6.0/Newtonsoft.Json.dll
```

### Resolved references (project.assets.json)

After `dotnet restore`, the full dependency graph is written to:

```
obj/project.assets.json
```

This file contains the resolved versions, target framework, and DLL paths for every transitive dependency.

## Approach 1: Require .csproj + dotnet restore

**How it works**:
1. User has a real .csproj with `<PackageReference>` elements
2. User runs `dotnet restore` (or glosharp does it)
3. GloSharp reads `obj/project.assets.json` to find all resolved assembly paths
4. GloSharp creates `MetadataReference` for each assembly

**Code sketch**:

```csharp
// Read project.assets.json
var assetsPath = Path.Combine(projectDir, "obj", "project.assets.json");
var assets = JsonDocument.Parse(File.ReadAllText(assetsPath));

// Extract resolved package paths
var references = new List<MetadataReference>();
// ... parse assets JSON for library paths ...

// Add framework references
var frameworkDir = GetFrameworkRefPath("net9.0");
foreach (var dll in Directory.GetFiles(frameworkDir, "*.dll"))
{
    references.Add(MetadataReference.CreateFromFile(dll));
}

// Create compilation
var compilation = CSharpCompilation.Create("GloSharpAnalysis",
    syntaxTrees: new[] { tree },
    references: references,
    options: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary));
```

**Pros**: Most reliable, handles transitive dependencies, matches real build behavior.
**Cons**: Requires a .csproj, requires `dotnet restore`, more setup for users.

## Approach 2: Use MSBuild API

**How it works**:
1. Load the .csproj via MSBuild's API
2. Evaluate the project to get resolved references
3. No need to manually parse project.assets.json

**Code sketch**:

```csharp
using Microsoft.Build.Locator;
using Microsoft.Build.Evaluation;

MSBuildLocator.RegisterDefaults();

var project = new Project("path/to/Example.csproj");
var references = project.GetItems("Reference")
    .Concat(project.GetItems("ReferencePath"))
    .Select(item => item.GetMetadataValue("HintPath"))
    .Where(path => !string.IsNullOrEmpty(path))
    .Select(path => MetadataReference.CreateFromFile(path));
```

**Pros**: Handles all MSBuild logic (conditions, imports, SDK resolution).
**Cons**: MSBuild API is heavy, slow to load, version-sensitive, hard to distribute.

## Approach 3: Standalone mode with default references

**How it works**:
1. No .csproj needed
2. Only use framework reference assemblies
3. Optionally support inline `// @nuget:` markers for simple package refs

**Code sketch**:

```csharp
// Find framework reference assemblies from the installed SDK
var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT")
    ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".dotnet");

var refPath = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
var latestVersion = Directory.GetDirectories(refPath)
    .OrderByDescending(d => d)
    .First();
var tfmDir = Path.Combine(latestVersion, "ref", "net9.0");

var references = Directory.GetFiles(tfmDir, "*.dll")
    .Select(dll => MetadataReference.CreateFromFile(dll))
    .ToList();
```

For `// @nuget:` markers, we'd need to:
1. Parse the marker
2. Download/resolve the package (via NuGet client libraries or `dotnet add package`)
3. Find the right TFM-specific DLL in the package

**Pros**: Zero-friction for simple snippets, no .csproj needed.
**Cons**: Limited to framework types without NuGet support, NuGet resolution from markers is complex.

## Approach 4: Use .NET Interactive's resolver

**How it works**:
.NET Interactive already handles `#r "nuget:..."` directives for notebooks. We could use a similar mechanism.

```csharp
#r "nuget: Newtonsoft.Json, 13.0.3"
using Newtonsoft.Json;

var json = JsonConvert.SerializeObject(new { Name = "test" });
```

**Pros**: Battle-tested NuGet resolution, familiar syntax.
**Cons**: Dependency on .NET Interactive, different marker syntax from glosharp's comment-based approach.

## Approach 5: File-based apps (.NET 10+)

**How it works**:
.NET 10 introduces [file-based apps](https://learn.microsoft.com/en-us/dotnet/core/sdk/file-based-apps) — single `.cs` files that can declare dependencies inline using `#:` directives. No `.csproj` needed. The SDK generates a virtual project and handles NuGet resolution automatically.

```csharp
#:package Newtonsoft.Json@13.0.3
#:property TargetFramework=net10.0

using Newtonsoft.Json;

var json = JsonConvert.SerializeObject(new { Name = "test" });
//                ^?
```

Key directives:
- `#:package Newtonsoft.Json@13.0.3` — adds a NuGet package reference
- `#:package Spectre.Console@*` — latest version
- `#:sdk Microsoft.NET.Sdk.Web` — switch SDK (e.g., for ASP.NET)
- `#:property TargetFramework=net10.0` — set any MSBuild property
- `#:project ../SharedLib/SharedLib.csproj` — reference another project

The SDK handles restore, build, and publish:
```bash
dotnet build file.cs       # compiles, resolves packages
dotnet run file.cs         # build + run
dotnet file.cs             # shorthand
```

**This is a natural fit for glosharp's lightweight mode.** Instead of inventing our own `// @nuget:` marker syntax, we could adopt the `#:` directive syntax that the SDK already understands. GloSharp could:

1. Detect `#:` directives in the source file
2. Use `dotnet build file.cs` to resolve packages and produce the compilation
3. Read the virtual project's output to find resolved assembly paths
4. Or: use the SDK's generated project to create our own Roslyn compilation

**Pros**: First-class SDK support, no custom NuGet resolution needed, familiar syntax for .NET 10+ users, packages declared inline in the snippet itself.
**Cons**: Requires .NET 10 SDK (ships late 2025), the `#:` directives are not valid C# syntax (they're preprocessor-level), need to strip them before Roslyn parsing if we do our own compilation.

### Implications for glosharp marker syntax

This changes the design equation. Rather than `// @nuget: Newtonsoft.Json@13.0.3` (a glosharp-specific invention), we could support:

```csharp
#:package Newtonsoft.Json@13.0.3

using Newtonsoft.Json;

var json = JsonConvert.SerializeObject(new { Name = "test" });
//                ^?
```

The `#:` lines would be stripped from the rendered output (like cut markers), but the file itself is valid for `dotnet build`. This means **the same file is both a glosharp snippet and a buildable .NET app** — no duplication, no custom tooling for package resolution.

## Approach 6: Compiler logs (complog) for portable compilations

**How it works**:
[complog](https://github.com/jaredpar/complog) by Jared Parsons (compiler team) creates self-contained compiler log files from MSBuild binary logs. These logs contain everything needed to recreate `Compilation` instances — all source, references, analyzers, compiler options — in a single portable file.

```bash
# In CI: build with binary log, then create complog
dotnet build -bl MySolution.sln
complog create msbuild.binlog

# On any machine: recreate the Compilation
# No need for the original project, NuGet cache, or SDK version
```

API usage:

```csharp
using Basic.CompilerLog.Util;

// Open a complog and recreate Compilation objects
using var reader = CompilerCallReaderUtil.Create("build.complog");
foreach (var compilationData in reader.ReadAllCompilationData())
{
    var compilation = compilationData.GetCompilationAfterGenerators();
    // Full Compilation with all references resolved
    // Can call GetSemanticModel(), GetDiagnostics(), etc.
}

// Or recreate a full Roslyn Workspace
var solutionReader = SolutionReader.Create("build.complog");
var workspace = new AdhocWorkspace();
var solution = workspace.AddSolution(solutionReader.ReadSolutionInfo());
```

**This opens up an interesting architecture for glosharp in documentation repos:**

1. CI builds the sample project with `-bl`, creating a binary log
2. `complog create` packages the compilation into a portable `.complog` file
3. GloSharp consumes the `.complog` directly — no need to resolve NuGet packages, find SDK paths, or run `dotnet restore`
4. The `.complog` can be committed to the docs repo or stored as a build artifact

```bash
# In the samples CI job:
dotnet build samples/ -bl
complog create msbuild.binlog -o samples.complog

# In the docs build:
glosharp process src/Example.cs --complog samples.complog
```

**Pros**: Completely portable (no SDK/NuGet needed on the docs build machine), captures the exact compilation state, handles source generators and analyzers, created by the compiler team so it tracks Roslyn closely.
**Cons**: Extra build step to create the complog, the complog includes all source/references (potentially large, and sensitive — it's the full compilation), adds a dependency on the complog tool/library. Also the compilation is a snapshot — if you edit the snippet source, the complog's compilation no longer matches.

### When complog makes sense for glosharp

The complog approach is most valuable when:
- The documentation build runs on a different machine from the code build (e.g., separate CI jobs)
- The project has complex dependencies that are expensive to resolve
- You want to guarantee the docs build uses exactly the same compilation as the tested code
- The build machine doesn't have the .NET SDK installed (e.g., a pure Node.js docs builder)

It's less useful for the "quick blog post" scenario where you just want to annotate a small snippet.

## Approach 7: Use the "trusted platform assemblies" list

**How it works**:
The running .NET process knows its own reference assemblies:

```csharp
var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES"))
    .Split(Path.PathSeparator);

var references = trustedAssemblies
    .Select(path => MetadataReference.CreateFromFile(path))
    .ToList();
```

**Pros**: Dead simple, no path detection needed.
**Cons**: Only includes assemblies available to the running process, not reference assemblies. May include implementation assemblies rather than ref assemblies (affects API surface visibility).

## Recommended approach

**A tiered strategy based on complexity:**

| Scenario | Approach | Why |
|---|---|---|
| Simple snippet (no NuGet) | **Approach 3** — standalone with framework refs | Zero friction, no project needed |
| Snippet with packages (.NET 10+) | **Approach 5** — file-based apps with `#:package` | SDK handles everything, `#:` directives are the standard |
| Full documentation project | **Approach 1** — .csproj + project.assets.json | Most reliable for complex dependency graphs |
| Separate build/docs machines | **Approach 6** — complog | Portable compilation, no SDK needed on docs machine |

### The file-based apps approach changes everything

With .NET 10's file-based apps, the "lightweight mode" problem is essentially solved by the SDK itself. Instead of inventing custom `// @nuget:` markers, we adopt `#:package` — a real SDK feature. The snippet file is simultaneously:
- A valid input for `dotnet build` (SDK resolves packages)
- A valid input for glosharp (we extract metadata)
- Self-documenting (dependencies are declared inline)

For `.NET 10+` targets, this should be the **default path**. Fall back to `.csproj` for older TFMs or complex projects.

### The complog approach for CI-separated builds

For documentation repos where the code build and doc build are separate CI jobs, complog offers a clean architecture: build once, analyze anywhere. The compilation artifact is the contract between the two jobs.

### Finding framework reference assemblies

```csharp
public static string? FindFrameworkRefPath(string tfm = "net9.0")
{
    // 1. Check DOTNET_ROOT
    var dotnetRoot = Environment.GetEnvironmentVariable("DOTNET_ROOT");

    // 2. Fall back to well-known paths
    if (string.IsNullOrEmpty(dotnetRoot))
    {
        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            dotnetRoot = @"C:\Program Files\dotnet";
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            dotnetRoot = "/usr/local/share/dotnet";
        else
            dotnetRoot = "/usr/share/dotnet";
    }

    var packsDir = Path.Combine(dotnetRoot, "packs", "Microsoft.NETCore.App.Ref");
    if (!Directory.Exists(packsDir)) return null;

    // Find latest version
    var latestPack = Directory.GetDirectories(packsDir)
        .OrderByDescending(d => Path.GetFileName(d))
        .FirstOrDefault();

    if (latestPack == null) return null;

    var refDir = Path.Combine(latestPack, "ref", tfm);
    return Directory.Exists(refDir) ? refDir : null;
}
```

## Open questions

- Should glosharp run `dotnet restore` / `dotnet build` automatically, or require the user to do it first?
- For file-based apps: can we hook into the SDK's virtual project to get resolved references without a full build? Or do we just `dotnet build file.cs` and read the output?
- How do we handle target framework selection? Default to latest installed? Allow override via `#:property TargetFramework=net10.0`?
- Should we support `<FrameworkReference>` beyond `Microsoft.NETCore.App` (e.g., `Microsoft.AspNetCore.App` via `#:sdk Microsoft.NET.Sdk.Web`)?
- How do we handle source generators and analyzers that affect the compilation? (complog handles this; file-based apps handle it via the SDK)
- For the complog approach: how large are typical complogs? Is it practical to commit them to a docs repo, or should they be CI artifacts?
- Should glosharp support both `#:package` (file-based apps) and `.csproj` (traditional) simultaneously, or pick one based on what's present?
- The `#:` directives are not valid C# — they're handled by the SDK before the compiler sees them. How do we parse them in glosharp's own pipeline?

## Key NuGet packages needed

```xml
<PackageReference Include="Microsoft.CodeAnalysis.CSharp" Version="4.*" />
<PackageReference Include="Microsoft.CodeAnalysis.CSharp.Features" Version="4.*" />
<!-- For MSBuild approach only: -->
<PackageReference Include="Microsoft.Build.Locator" Version="1.*" />
<PackageReference Include="Microsoft.Build" Version="17.*" />
```
