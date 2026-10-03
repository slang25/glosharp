namespace GloSharp.Core;

public interface ICompilationContextResolver : IDisposable
{
    /// <summary>
    /// Selects one compilation from the context and returns its references and options.
    /// </summary>
    /// <param name="projectName">
    /// Project to select: <c>Lib</c>, <c>Lib.csproj</c>, or <c>Lib (net8.0)</c> to also pick
    /// the target framework. Null selects the first compilation, with a warning in
    /// <see cref="ComplogResolutionResult.Warnings"/> when there is more than one project.
    /// </param>
    /// <param name="targetFramework">
    /// Picks among a multi-targeted project's compilations (case-insensitive). Ignored, with a
    /// warning, when every candidate targets the same single framework.
    /// </param>
    ComplogResolutionResult Resolve(string? projectName = null, string? targetFramework = null);
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
        public ComplogResolutionResult Resolve(string? projectName = null, string? targetFramework = null)
            => _inner.Resolve(projectName, targetFramework);
        public void Dispose() => _inner.Dispose();
    }

    private sealed class GloContextResolverAdapter : ICompilationContextResolver
    {
        private readonly GloContextResolver _inner;
        public GloContextResolverAdapter(GloContextResolver inner) { _inner = inner; }
        public ComplogResolutionResult Resolve(string? projectName = null, string? targetFramework = null)
            => _inner.Resolve(projectName, targetFramework);
        public void Dispose() => _inner.Dispose();
    }
}

/// <summary>
/// Picks one compilation out of a complog/.glocontext by project name and target framework,
/// shared by <see cref="ComplogResolver"/> and <see cref="GloContextResolver"/> so both
/// accept the same selectors and report ambiguity the same way.
/// </summary>
internal static class CompilationSelector
{
    public static T Select<T>(
        IReadOnlyList<T> compilations,
        Func<T, string> projectNameOf,
        Func<T, string?> targetFrameworkOf,
        string? projectName,
        string? targetFramework,
        string sourceLabel,
        List<string> warnings)
    {
        if (compilations.Count == 0)
            throw new InvalidOperationException($"{sourceLabel} contains no C# compilations.");

        string Describe(T c) =>
            string.IsNullOrEmpty(targetFrameworkOf(c)) ? projectNameOf(c) : $"{projectNameOf(c)} ({targetFrameworkOf(c)})";
        var available = string.Join(", ", compilations.Select(Describe).Distinct());

        var (name, tfmFromName) = ParseSelector(projectName);
        var requestedTfm = tfmFromName ?? targetFramework;

        IReadOnlyList<T> candidates = compilations;
        if (name != null)
        {
            candidates = compilations
                .Where(c => string.Equals(projectNameOf(c), name, StringComparison.OrdinalIgnoreCase))
                .ToList();
            if (candidates.Count == 0)
                throw new InvalidOperationException(
                    $"Project '{projectName}' not found in {sourceLabel}. Available projects: {available}");
        }

        if (requestedTfm != null)
        {
            var byTfm = candidates
                .Where(c => string.Equals(targetFrameworkOf(c), requestedTfm, StringComparison.OrdinalIgnoreCase))
                .ToList();
            var candidateTfms = candidates.Select(targetFrameworkOf).Distinct(StringComparer.OrdinalIgnoreCase).ToList();

            if (byTfm.Count > 0)
            {
                candidates = byTfm;
            }
            else if (tfmFromName != null || candidateTfms.Count > 1)
            {
                throw new InvalidOperationException(
                    $"No compilation for target framework '{requestedTfm}'" +
                    (name != null ? $" of project '{name}'" : "") +
                    $" in {sourceLabel}. Available: {available}");
            }
            else
            {
                warnings.Add(
                    $"Target framework '{requestedTfm}' was requested, but the selected compilation in {sourceLabel} " +
                    $"targets {candidateTfms[0]}; using {candidateTfms[0]}.");
            }
        }

        var selected = candidates[0];
        var distinct = candidates.Select(Describe).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (distinct.Count > 1)
        {
            var sameProject = candidates.Select(projectNameOf).Distinct(StringComparer.OrdinalIgnoreCase).Count() == 1;
            warnings.Add(
                $"{sourceLabel} contains {distinct.Count} candidate compilations ({string.Join(", ", distinct)}); " +
                $"using {Describe(selected)}. " +
                (sameProject
                    ? "Pass --framework to choose a target framework."
                    : "Pass --complog-project (e.g. --complog-project \"Name (tfm)\") to choose one."));
        }

        return selected;
    }

    /// <summary>Parses <c>Lib</c>, <c>Lib.csproj</c>, <c>Lib (net8.0)</c> and <c>Lib(net8.0)</c>.</summary>
    internal static (string? Name, string? Tfm) ParseSelector(string? selector)
    {
        if (string.IsNullOrWhiteSpace(selector))
            return (null, null);

        var s = selector.Trim();
        string? tfm = null;
        if (s.EndsWith(')') && s.LastIndexOf('(') is var open and > 0)
        {
            tfm = s[(open + 1)..^1].Trim();
            s = s[..open].Trim();
            if (tfm.Length == 0) tfm = null;
        }

        if (s.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            s = s[..^".csproj".Length];

        return (s, tfm);
    }
}
