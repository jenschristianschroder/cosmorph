using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace Cosmorph.Application.Serialization;

/// <summary>
/// Canonical JSON settings. Serialization is byte-stable so the same seed and inputs produce a
/// byte-equivalent snapshot and Chronicle.
/// </summary>
public static class CanonicalJson
{
    public static JsonSerializerOptions Options { get; } = Create();

    public static string Serialize<T>(T value) => JsonSerializer.Serialize(value, Options);

    public static T Deserialize<T>(string json) =>
        JsonSerializer.Deserialize<T>(json, Options) ?? throw new JsonException("Document deserialized to null.");

    private static JsonSerializerOptions Create()
    {
        var options = new JsonSerializerOptions(JsonSerializerDefaults.General)
        {
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.Never,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            NumberHandling = JsonNumberHandling.Strict,
            // Reject unknown members so a malformed or hostile document never silently loads.
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };

        options.Converters.Add(new WorldIdJsonConverter());
        options.Converters.Add(new WardenIdJsonConverter());
        options.Converters.Add(new SpeciesIdJsonConverter());
        options.TypeInfoResolver = new DefaultJsonTypeInfoResolver();
        options.MakeReadOnly();
        return options;
    }
}
