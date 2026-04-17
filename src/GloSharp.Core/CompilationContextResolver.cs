namespace GloSharp.Core;

public interface ICompilationContextResolver : IDisposable
{
    ComplogResolutionResult Resolve(string? projectName = null);
}

public static class CompilationContextResolverFactory
{
    public static ICompilationContextResolver Open(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException($"Compilation context file not found: {path}", path);

        Span<byte> header = stackalloc byte[GloContextFormat.HeaderSize];
        using (var fs = File.OpenRead(path))
        {
            var read = fs.Read(header);
            if (read < GloContextFormat.Magic.Length)
                throw new InvalidDataException(
                    $"File '{path}' is too small to be a .complog or .glocontext.");
        }

        if (GloContextFormat.LooksLikeGloContext(header))
            return new GloContextResolverAdapter(GloContextResolver.Open(path));

        if (GloContextFormat.LooksLikeZip(header))
            return new ComplogResolverAdapter(ComplogResolver.Open(path));

        throw new InvalidDataException(
            $"File '{path}' is neither a .glocontext (GLOCTX magic) nor a .complog (zip archive).");
    }

    private sealed class ComplogResolverAdapter : ICompilationContextResolver
    {
        private readonly ComplogResolver _inner;
        public ComplogResolverAdapter(ComplogResolver inner) { _inner = inner; }
        public ComplogResolutionResult Resolve(string? projectName = null) => _inner.Resolve(projectName);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class GloContextResolverAdapter : ICompilationContextResolver
    {
        private readonly GloContextResolver _inner;
        public GloContextResolverAdapter(GloContextResolver inner) { _inner = inner; }
        public ComplogResolutionResult Resolve(string? projectName = null) => _inner.Resolve(projectName);
        public void Dispose() => _inner.Dispose();
    }
}
