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

        var targetFramework = selectedCall.TargetFramework ?? "net8.0";

        return new ComplogResolutionResult
        {
            References = references,
            CompilationOptions = (CSharpCompilationOptions)compilation.Options,
            ParseOptions = (CSharpParseOptions)compilationData.ParseOptions,
            TargetFramework = targetFramework,
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        if (_reader is IDisposable disposable)
            disposable.Dispose();
    }
}
