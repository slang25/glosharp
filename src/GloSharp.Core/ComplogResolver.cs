using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace GloSharp.Core;

public class ComplogResolutionResult
{
    public required List<MetadataReference> References { get; init; }
    public required CSharpCompilationOptions CompilationOptions { get; init; }
    public required CSharpParseOptions ParseOptions { get; init; }
    public required string TargetFramework { get; init; }

    /// <summary>The selected compilation's project name.</summary>
    public string ProjectName { get; init; } = "";

    /// <summary>
    /// Non-fatal problems the user should see (surface them in <c>meta.warnings</c>), e.g.
    /// that one of several projects was picked implicitly.
    /// </summary>
    public List<string> Warnings { get; init; } = [];
}

public class ComplogResolver : IDisposable
{
    private readonly ICompilerCallReader _reader;
    private bool _disposed;

    private ComplogResolver(ICompilerCallReader reader)
    {
        _reader = reader;
    }

    public static ComplogResolver Open(string complogPath)
    {
        if (!File.Exists(complogPath))
            throw new FileNotFoundException($"Complog file not found: {complogPath}", complogPath);

        var reader = CompilerCallReaderUtil.Create(complogPath, BasicAnalyzerKind.None);
        return new ComplogResolver(reader);
    }

    public ComplogResolutionResult Resolve(string? projectName = null, string? targetFramework = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var calls = _reader.ReadAllCompilerCalls(c => c.IsCSharp);
        if (calls.Count == 0)
            throw new InvalidOperationException(
                "Complog contains no C# compilations. If it was created from an incremental build, " +
                "rebuild with 'dotnet build --no-incremental -bl' so every project is compiled.");

        var warnings = new List<string>();
        var selectedCall = CompilationSelector.Select(
            calls,
            c => Path.GetFileNameWithoutExtension(c.ProjectFileName),
            c => c.TargetFramework,
            projectName,
            targetFramework,
            "complog",
            warnings);

        var compilationData = _reader.ReadCompilationData(selectedCall);
        var compilation = (CSharpCompilation)compilationData.GetCompilationAfterGenerators();

        // The compilation's references only carry the file name; the reference data records
        // the original full path, which is what locates the XML docs.
        var originalPaths = new Dictionary<Guid, string>();
        foreach (var data in _reader.ReadAllReferenceData(selectedCall))
        {
            if (!string.IsNullOrEmpty(data.FilePath))
                originalPaths.TryAdd(data.Mvid, data.FilePath);
        }

        var references = compilation.References
            .Select(r => r is PortableExecutableReference pe ? WithDocumentation(pe, originalPaths) : r)
            .ToList();

        return new ComplogResolutionResult
        {
            References = references,
            CompilationOptions = (CSharpCompilationOptions)compilation.Options,
            ParseOptions = (CSharpParseOptions)compilationData.ParseOptions,
            TargetFramework = selectedCall.TargetFramework ?? FrameworkResolver.DefaultTargetFramework,
            ProjectName = Path.GetFileNameWithoutExtension(selectedCall.ProjectFileName),
            Warnings = warnings,
        };
    }

    /// <summary>
    /// Complog references carry no documentation. Re-create the reference with the XML docs
    /// found beside its original path, or — for targeting-pack assemblies whose original path
    /// doesn't exist on this machine — in a local copy of the same pack.
    /// </summary>
    private static MetadataReference WithDocumentation(
        PortableExecutableReference reference, IReadOnlyDictionary<Guid, string> originalPaths)
    {
        try
        {
            if (reference.GetMetadata() is not AssemblyMetadata metadata)
                return reference;

            var modules = metadata.GetModules();
            var originalPath = modules.Length > 0 && originalPaths.TryGetValue(modules[0].GetModuleVersionId(), out var p)
                ? p
                : reference.FilePath;
            var xmlPath = XmlDocumentationLocator.Find(originalPath);
            if (xmlPath == null)
                return reference;

            return metadata.GetReference(
                documentation: XmlDocumentationProvider.CreateFromFile(xmlPath),
                aliases: reference.Properties.Aliases,
                embedInteropTypes: reference.Properties.EmbedInteropTypes,
                filePath: reference.FilePath,
                display: reference.Display);
        }
        catch (Exception ex) when (ex is IOException or BadImageFormatException or NotSupportedException)
        {
            return reference;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_reader is IDisposable disposable)
            disposable.Dispose();
    }
}

/// <summary>Finds XML documentation files for reference assemblies.</summary>
internal static class XmlDocumentationLocator
{
    private static readonly Lazy<ReferencePackResolver> LocalPacks =
        new(() => ReferencePackResolver.CreateForReading(allowDownload: false));

    /// <summary>
    /// The .xml beside <paramref name="assemblyPath"/>; else, when the path is a targeting-pack
    /// assembly (…/Microsoft.NETCore.App.Ref/10.0.9/ref/net10.0/X.dll), the same file in a local
    /// copy of that pack (installed SDK, NuGet cache, glosharp cache). Never downloads.
    /// </summary>
    public static string? Find(string? assemblyPath)
    {
        if (string.IsNullOrEmpty(assemblyPath))
            return null;

        try
        {
            var beside = Path.ChangeExtension(assemblyPath, ".xml");
            if (File.Exists(beside))
                return beside;

            return ComplogCompactor.TryParsePackOrigin(assemblyPath) is { } origin
                ? LocalPacks.Value.FindDocumentation(origin.Pack, origin.RelativePath)
                : null;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException)
        {
            return null;
        }
    }
}
