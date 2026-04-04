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

    public static string Serialize(GloSharpResult result)
    {
        return JsonSerializer.Serialize(result, Options);
    }
}
