using System.Text.Json;
using System.Text.Json.Serialization;

namespace TwoHash.Core;

public static class JsonOutput
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        WriteIndented = true,
    };

    public static string Serialize(TwohashResult result)
    {
        return JsonSerializer.Serialize(result, Options);
    }
}
