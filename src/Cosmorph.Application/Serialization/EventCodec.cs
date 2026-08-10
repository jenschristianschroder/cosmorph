using Cosmorph.Domain.Events;
using Cosmorph.Domain.Worlds;

namespace Cosmorph.Application.Serialization;

/// <summary>Persisted Chronicle entry with an explicit schema version.</summary>
public sealed record EventDocument
{
    public const string CurrentSchema = "world-event/1";

    public required string Schema { get; init; }

    public required long Sequence { get; init; }

    public required long Tick { get; init; }

    public required int Type { get; init; }

    public required int Chapter { get; init; }

    public int? CellIndex { get; init; }

    public string? Species { get; init; }

    public required int Magnitude { get; init; }

    public string? Narration { get; init; }

    public required string SimulationVersion { get; init; }

    public required string ContentVersion { get; init; }
}

/// <summary>Converts between Chronicle events and their persisted documents.</summary>
public static class EventCodec
{
    public static EventDocument ToDocument(WorldEvent worldEvent)
    {
        ArgumentNullException.ThrowIfNull(worldEvent);
        return new EventDocument
        {
            Schema = EventDocument.CurrentSchema,
            Sequence = worldEvent.Sequence,
            Tick = worldEvent.Tick,
            Type = (int)worldEvent.Type,
            Chapter = worldEvent.Chapter.Value,
            CellIndex = worldEvent.CellIndex,
            Species = worldEvent.Species?.Value,
            Magnitude = worldEvent.Magnitude,
            Narration = worldEvent.Narration,
            SimulationVersion = worldEvent.SimulationVersion,
            ContentVersion = worldEvent.ContentVersion,
        };
    }

    public static WorldEvent ToEvent(EventDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        if (!string.Equals(document.Schema, EventDocument.CurrentSchema, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Unsupported event schema '{document.Schema}'.");
        }

        return new WorldEvent
        {
            Sequence = document.Sequence,
            Tick = document.Tick,
            Type = (WorldEventType)document.Type,
            Chapter = new ChapterId(document.Chapter),
            CellIndex = document.CellIndex,
            Species = document.Species is null ? null : new SpeciesId(document.Species),
            Magnitude = document.Magnitude,
            Narration = document.Narration,
            SimulationVersion = document.SimulationVersion,
            ContentVersion = document.ContentVersion,
        };
    }
}
