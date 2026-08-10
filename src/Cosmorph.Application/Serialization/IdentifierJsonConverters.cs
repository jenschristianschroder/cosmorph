using System.Text.Json;
using System.Text.Json.Serialization;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Serialization;

/// <summary>
/// Validated identifiers are written as plain strings and parsed back through their validating
/// factory, so a stored or received document can never introduce an invalid identifier.
/// </summary>
public sealed class WorldIdJsonConverter : JsonConverter<WorldId>
{
    public override WorldId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        WorldId.TryParse(reader.GetString(), out var id) ? id : throw new JsonException("Invalid world identifier.");

    public override void Write(Utf8JsonWriter writer, WorldId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

/// <inheritdoc cref="WorldIdJsonConverter"/>
public sealed class WardenIdJsonConverter : JsonConverter<WardenId>
{
    public override WardenId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options) =>
        WardenId.TryParse(reader.GetString(), out var id) ? id : throw new JsonException("Invalid Warden identifier.");

    public override void Write(Utf8JsonWriter writer, WardenId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}

/// <summary>Species identifiers are bounded content keys and are written as plain strings.</summary>
public sealed class SpeciesIdJsonConverter : JsonConverter<SpeciesId>
{
    public const int MaxLength = 40;

    public override SpeciesId Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var value = reader.GetString();
        if (string.IsNullOrEmpty(value) || value.Length > MaxLength)
        {
            throw new JsonException("Invalid species identifier.");
        }

        foreach (var character in value)
        {
            if (!char.IsAsciiLetterLower(character) && !char.IsAsciiDigit(character) && character != '-')
            {
                throw new JsonException("Invalid species identifier.");
            }
        }

        return new SpeciesId(value);
    }

    public override void Write(Utf8JsonWriter writer, SpeciesId value, JsonSerializerOptions options)
    {
        ArgumentNullException.ThrowIfNull(writer);
        writer.WriteStringValue(value.Value);
    }
}
