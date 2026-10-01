using System.Text.Json;
using System.Text.Json.Serialization;

namespace GloSharp.Core;

public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    private static readonly JsonSerializerOptions CompactOptions = new(Options) { WriteIndented = false };

    public static string Serialize(GloSharpResult result)
    {
        return JsonSerializer.Serialize(result, Options);
    }

    /// <summary>
    /// The same JSON as <see cref="Serialize(GloSharpResult)"/> on a single line, for
    /// line-delimited protocols such as <c>glosharp serve</c>.
    /// </summary>
    public static string SerializeCompact(GloSharpResult result)
    {
        return JsonSerializer.Serialize(result, CompactOptions);
    }
}
