using Basic.CompilerLog.Util;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace TwoHash.Core;

public class ComplogResolutionResult
{
    public required List<MetadataReference> References { get; init; }
    public required CSharpCompilationOptions CompilationOptions { get; init; }
    public required CSharpParseOptions ParseOptions { get; init; }
    public required string TargetFramework { get; init; }
    public required List<PackageReference> Packages { get; init; }
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

    public ComplogResolutionResult Resolve(string? projectName = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        var calls = _reader.ReadAllCompilerCalls(c => c.IsCSharp);
        if (calls.Count == 0)
            throw new InvalidOperationException("Complog contains no C# compilations.");

        CompilerCall selectedCall;
        if (projectName != null)
        {
            selectedCall = calls.FirstOrDefault(c =>
                string.Equals(Path.GetFileNameWithoutExtension(c.ProjectFileName), projectName, StringComparison.OrdinalIgnoreCase))
                ?? throw new InvalidOperationException(
                    $"Project '{projectName}' not found in complog. Available projects: {string.Join(", ", calls.Select(c => Path.GetFileNameWithoutExtension(c.ProjectFileName)))}");
        }
        else
        {
            selectedCall = calls[0];
        }

        var compilationData = _reader.ReadCompilationData(selectedCall);
        var compilation = (CSharpCompilation)compilationData.GetCompilationAfterGenerators();

        var references = compilation.References.ToList();

        var packages = ExtractPackages(_reader.ReadAllReferenceData(selectedCall));

        var targetFramework = selectedCall.TargetFramework ?? "net8.0";

        return new ComplogResolutionResult
        {
            References = references,
            CompilationOptions = (CSharpCompilationOptions)compilation.Options,
            ParseOptions = (CSharpParseOptions)compilationData.ParseOptions,
            TargetFramework = targetFramework,
            Packages = packages,
        };
    }

    internal static List<PackageReference> ExtractPackages(List<ReferenceData> referenceDataList)
    {
        var packages = new List<PackageReference>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var refData in referenceDataList)
        {
            var filePath = refData.FilePath;
            if (string.IsNullOrEmpty(filePath))
                continue;

            // NuGet packages live under paths like:
            // ~/.nuget/packages/<package-id>/<version>/lib/<tfm>/<assembly>.dll
            var nugetIndex = filePath.IndexOf(".nuget/packages/", StringComparison.OrdinalIgnoreCase);
            if (nugetIndex < 0)
                nugetIndex = filePath.IndexOf(".nuget\\packages\\", StringComparison.OrdinalIgnoreCase);
            if (nugetIndex < 0)
                continue;

            var afterPackages = filePath[(nugetIndex + ".nuget/packages/".Length)..];
            var parts = afterPackages.Split('/', '\\');
            if (parts.Length < 2)
                continue;

            var packageId = parts[0];
            var version = parts[1];

            if (seen.Add(packageId))
            {
                packages.Add(new PackageReference
                {
                    Name = packageId,
                    Version = version,
                });
            }
        }

        return packages;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_reader is IDisposable disposable)
            disposable.Dispose();
    }
}
