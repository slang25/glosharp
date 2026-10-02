using System.Collections.Concurrent;
using Microsoft.CodeAnalysis;

namespace GloSharp.Core;

/// <summary>
/// Process-wide cache of file-backed <see cref="MetadataReference"/>s (with their XML docs).
/// </summary>
/// <remarks>
/// References are immutable and safe to share between compilations, and Roslyn reuses the
/// metadata it has already read for a shared reference object. Creating them afresh for every
/// compilation context re-reads ~170 framework assemblies and their XML docs each time, which
/// is slow in a long-lived process (<c>glosharp serve</c>) and, with many contexts in flight
/// at once (parallel tests), exhausts memory. Entries are keyed by path, size and timestamp,
/// so a rebuilt project output gets a fresh reference.
/// </remarks>
internal static class MetadataReferenceCache
{
    private static readonly ConcurrentDictionary<string, Lazy<MetadataReference>> Cache = new(StringComparer.Ordinal);

    public static MetadataReference Get(string path)
    {
        var info = new FileInfo(path);
        var xmlPath = Path.ChangeExtension(path, ".xml");
        var xml = new FileInfo(xmlPath);
        var key = $"{info.FullName}\0{info.Length}\0{info.LastWriteTimeUtc.Ticks}\0{(xml.Exists ? xml.LastWriteTimeUtc.Ticks : 0)}";

        return Cache.GetOrAdd(key, _ => new Lazy<MetadataReference>(() =>
        {
            var docProvider = xml.Exists ? XmlDocumentationProvider.CreateFromFile(xmlPath) : null;
            return MetadataReference.CreateFromFile(path, documentation: docProvider);
        })).Value;
    }
}
