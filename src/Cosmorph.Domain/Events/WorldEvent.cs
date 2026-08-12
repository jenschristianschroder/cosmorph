using Cosmorph.Domain.Worlds;

namespace Cosmorph.Domain.Events;

/// <summary>Stable event type names. Values are part of the persisted Chronicle format.</summary>
public enum WorldEventType
{
    WorldCreated = 0,
    Drought = 1,
    Disease = 2,
    Fire = 3,
    Flood = 4,
    FoodShortage = 5,
    Migration = 6,
    Recovery = 7,
    PopulationMilestone = 8,
    WardenAction = 9,
    WardenProposalRejected = 10,
    WorldmindDecision = 11,
    WorldmindFallback = 12,
    TimeCompressed = 13,
    ChapterClosed = 14,
    ConstructionRaised = 15,
    ConstructionLost = 16,
}

/// <summary>
/// One entry of the append-only Chronicle. Sequence numbers are strictly increasing per world.
/// </summary>
public sealed record WorldEvent
{
    public const string TypeVersion = "world-event/1";

    public required long Sequence { get; init; }

    public required long Tick { get; init; }

    public required WorldEventType Type { get; init; }

    public required ChapterId Chapter { get; init; }

    public int? CellIndex { get; init; }

    public SpeciesId? Species { get; init; }

    /// <summary>Bounded integer magnitude whose meaning depends on the event type.</summary>
    public int Magnitude { get; init; }

    /// <summary>Spectator narration. Engine-authored or Worldmind-supplied; never executable.</summary>
    public string? Narration { get; init; }

    public required string SimulationVersion { get; init; }

    public required string ContentVersion { get; init; }
}
